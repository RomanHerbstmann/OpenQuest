using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenQuest.Api.Data;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Eventing;

/// <summary>
/// Turns the importer's <c>sync_finished</c> notification (see <c>mark_succeeded</c> in the importer) into an
/// <see cref="AssetSyncCompleted"/> event in the outbox, so that everything that depends on the asset data reacts right after a sync.
/// Like the <see cref="OutboxProcessor"/> it listens instead of polling. A notification is lost when this service is down, so after
/// connecting it announces the runs of the last days that have no event yet (idempotent: an announced run is skipped).
/// </summary>
public sealed class SyncFinishedListener(
    IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, ILogger<SyncFinishedListener> log) : BackgroundService
{
    public const string Channel = "sync_finished";
    private static readonly TimeSpan CatchUpWindow = TimeSpan.FromDays(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RunAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                log.LogError(e, "Sync listener failed; reconnecting.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var listener = new NpgsqlConnection(configuration.GetConnectionString("Default"));
        await listener.OpenAsync(ct);
        var runs = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
        listener.Notification += (_, e) => { if (Guid.TryParse(e.Payload, out var id)) runs.Writer.TryWrite(id); };
        await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", listener)) await listen.ExecuteNonQueryAsync(ct);

        await AnnounceMissedAsync(ct);   // after LISTEN, so a run that finishes meanwhile is not missed

        using var listenerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var listening = Task.Run(async () =>
        {
            try { while (!listenerCts.IsCancellationRequested) await listener.WaitAsync(listenerCts.Token); }
            finally { runs.Writer.TryComplete(); }
        }, CancellationToken.None);

        try
        {
            await foreach (var runId in runs.Reader.ReadAllAsync(ct))
            {
                try { await AnnounceAsync(runId, ct); }
                catch (Exception e) when (!ct.IsCancellationRequested) { log.LogError(e, "Announcing sync run {RunId} failed.", runId); }
            }
            await listening;   // the channel only ends when the listener stopped: rethrow its failure
        }
        finally
        {
            listenerCts.Cancel();
            try { await listening; } catch { /* shutting down or already reported */ }
        }
    }

    private async Task AnnounceMissedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var since = clock.GetUtcNow() - CatchUpWindow;
        var runIds = await db.SyncRuns.AsNoTracking().Where(r => r.Status == RunStatus.Succeeded && r.FinishedAt >= since)
            .OrderBy(r => r.FinishedAt).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in runIds) await AnnounceAsync(id, ct);
    }

    /// <summary>Publishes the event for a succeeded run unless there is one already. Returns whether an event was created.</summary>
    private async Task<bool> AnnounceAsync(Guid runId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.SyncRuns.AsNoTracking().Include(r => r.DataSource)
            .FirstOrDefaultAsync(r => r.Id == runId && r.Status == RunStatus.Succeeded, ct);
        if (run is null) return false;

        var known = await db.OutboxMessages.AsNoTracking().AnyAsync(m => m.Type == nameof(AssetSyncCompleted)
            && EF.Functions.JsonContains(m.Payload, "{\"runId\":\"" + runId + "\"}"), ct);
        if (known) return false;

        scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(
            new AssetSyncCompleted(run.Id, run.DataSourceId, run.DataSource.Key, run.AssetsCreated, run.AssetsUpdated, run.AssetsRemoved));
        await db.SaveChangesAsync(ct);
        log.LogInformation("Sync run {RunId} of {Source} announced.", run.Id, run.DataSource.Key);
        return true;
    }
}
