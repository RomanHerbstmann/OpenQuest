using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Core.Adapters;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Sync;

public sealed class ConfiguredAdapterProvider(IEnumerable<IDataSourceAdapter> adapters, IOptions<AdapterOptions> options) : IAdapterProvider
{
    public IDataSourceAdapter Active =>
        adapters.FirstOrDefault(a => a.Id == options.Value.Active)
        ?? throw new InvalidOperationException(
            $"Adapter '{options.Value.Active}' is not registered. Known: {string.Join(", ", adapters.Select(a => a.Id))}");
}

/// <summary>
/// Imports assets from the active adapter into our own database (one sync_run per data source).
/// The open data platform is not a runtime dependency of the game. The city offers no change notifications,
/// so this is the one place where pulling on a schedule is unavoidable.
/// </summary>
public sealed class AssetSyncService(
    IServiceScopeFactory scopes,
    IAdapterProvider adapters,
    IAssetBatchUpserter upserter,
    TimeProvider clock,
    ILogger<AssetSyncService> log) : IAssetSynchronizer, ISyncTrigger, ISyncStatus
{
    private const int BatchSize = 1000;
    /// <summary>Refuse to mark assets as removed if the source suddenly delivers far fewer than before.</summary>
    private const double MinShareOfPreviousCount = 0.5;

    private readonly SemaphoreSlim _running = new(1, 1);

    public bool IsRunning => _running.CurrentCount == 0;

    public bool TryStartInBackground()
    {
        if (!_running.Wait(0)) return false;
        _ = Task.Run(async () =>
        {
            try { await SyncAllAsync(CancellationToken.None); }
            catch (Exception e) { log.LogError(e, "Background asset sync failed."); }
            finally { _running.Release(); }
        });
        return true;
    }

    public async Task<IReadOnlyList<SyncRun>?> RunAsync(CancellationToken ct)
    {
        if (!await _running.WaitAsync(0, ct)) return null;
        try { return await SyncAllAsync(ct); }
        finally { _running.Release(); }
    }

    private async Task<IReadOnlyList<SyncRun>> SyncAllAsync(CancellationToken ct)
    {
        var adapter = adapters.Active;
        using var scope = scopes.CreateScope();
        var activeKeys = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DataSources
            .Where(d => d.AdapterKey == adapter.Id && d.IsActive).Select(d => d.Key).ToListAsync(ct);

        var runs = new List<SyncRun>();
        foreach (var d in adapter.DataSources.Where(d => activeKeys.Contains(d.Key)))
            runs.Add(await SyncDataSourceAsync(adapter, d, ct));
        return runs;
    }

    private async Task<SyncRun> SyncDataSourceAsync(IDataSourceAdapter adapter, DataSourceDescriptor descriptor, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = await db.DataSources.FirstAsync(d => d.Key == descriptor.Key, ct);
        var assetType = await db.AssetTypes.FirstAsync(t => t.Key == descriptor.AssetType.Key, ct);

        var run = new SyncRun { DataSourceId = source.Id, StartedAt = clock.GetUtcNow() };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Sync {Source} started (run {Id}).", source.Key, run.Id);

        try
        {
            var previousActive = await db.Assets.CountAsync(a => a.DataSourceId == source.Id && a.Status == AssetStatus.Active, ct);
            var hadAssets = await db.Assets.AnyAsync(a => a.DataSourceId == source.Id, ct);
            var context = new UpsertContext(source.Id, assetType.Id, run.StartedAt, hadAssets);
            var seen = 0;
            var batch = new List<Asset>(BatchSize);

            async Task FlushAsync()
            {
                if (batch.Count == 0) return;
                var outcome = await upserter.UpsertAsync(db, context, batch, ct);
                seen += batch.Count;
                run.AssetsCreated += outcome.Created;
                run.AssetsUpdated += outcome.Updated;
                batch.Clear();
            }

            await foreach (var asset in adapter.FetchAssets(new AssetQuery(descriptor.AssetType), ct))
            {
                batch.Add(asset);
                if (batch.Count >= BatchSize) await FlushAsync();
            }
            await FlushAsync();

            if (seen == 0)
                throw new InvalidOperationException("Source returned no assets; refusing to mark existing ones as removed.");
            if (previousActive > 0 && seen < previousActive * MinShareOfPreviousCount)
                throw new InvalidOperationException(
                    $"Source returned {seen} assets but {previousActive} were active before; refusing to mark the rest as removed. Check the source.");

            // Assets that vanished stay in the DB (quests reference them) but are flagged.
            run.AssetsRemoved = await db.Assets
                .Where(a => a.DataSourceId == source.Id && a.Status == AssetStatus.Active && a.LastSeenAt < run.StartedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AssetStatus.RemovedAtSource)
                                          .SetProperty(a => a.UpdatedAt, clock.GetUtcNow()), ct);

            run.Status = RunStatus.Succeeded;
            log.LogInformation("Sync {Source} done: {Created} created, {Updated} updated, {Removed} removed.",
                source.Key, run.AssetsCreated, run.AssetsUpdated, run.AssetsRemoved);
        }
        catch (Exception e)
        {
            run.Status = RunStatus.Failed;
            run.Error = e.Message.Length > 2000 ? e.Message[..2000] : e.Message;
            log.LogError(e, "Sync {Source} failed.", source.Key);
        }

        db.ChangeTracker.Clear();
        run.FinishedAt = clock.GetUtcNow();
        db.SyncRuns.Update(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }
}
