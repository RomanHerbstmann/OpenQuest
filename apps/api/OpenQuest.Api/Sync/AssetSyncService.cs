using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Api.Storage;
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
    IBlobWriter blobs,
    TimeProvider clock,
    ILogger<AssetSyncService> log) : IAssetSynchronizer, ISyncTrigger, ISyncStatus
{
    private const int BatchSize = 1000;
    /// <summary>Refuse to mark assets as removed if the source suddenly delivers far fewer than before.</summary>
    private const double MinShareOfPreviousCount = 0.5;

    private readonly SemaphoreSlim _running = new(1, 1);

    public bool IsRunning => _running.CurrentCount == 0;

    public bool TryStartInBackground(bool acceptSchemaChange = false)
    {
        if (!_running.Wait(0)) return false;
        _ = Task.Run(async () =>
        {
            try { await SyncAllAsync(acceptSchemaChange, CancellationToken.None); }
            catch (Exception e) { log.LogError(e, "Background asset sync failed."); }
            finally { _running.Release(); }
        });
        return true;
    }

    public async Task<IReadOnlyList<SyncRun>?> RunAsync(bool acceptSchemaChange, CancellationToken ct)
    {
        if (!await _running.WaitAsync(0, ct)) return null;
        try { return await SyncAllAsync(acceptSchemaChange, ct); }
        finally { _running.Release(); }
    }

    private async Task<IReadOnlyList<SyncRun>> SyncAllAsync(bool acceptSchemaChange, CancellationToken ct)
    {
        var adapter = adapters.Active;
        using var scope = scopes.CreateScope();
        var activeKeys = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DataSources
            .Where(d => d.AdapterKey == adapter.Id && d.IsActive).Select(d => d.Key).ToListAsync(ct);

        var runs = new List<SyncRun>();
        foreach (var d in adapter.DataSources.Where(d => activeKeys.Contains(d.Key)))
            runs.Add(await SyncDataSourceAsync(adapter, d, acceptSchemaChange, ct));
        return runs;
    }

    private async Task<SyncRun> SyncDataSourceAsync(IDataSourceAdapter adapter, DataSourceDescriptor descriptor, bool acceptSchemaChange, CancellationToken ct)
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

            var snapshot = await adapter.FetchSnapshotAsync(new AssetQuery(descriptor.AssetType), ct);
            run.RecordCount = snapshot.RecordCount;
            run.SchemaHash = snapshot.SchemaHash;

            // Keep the download exactly as delivered, before anything can go wrong, so every snapshot can be reloaded
            // (also the one of a failed run: that is the evidence for what the source sent).
            run.SnapshotKey = $"snapshots/{source.Key}/{run.Id}.{snapshot.RawFileExtension}";
            await blobs.PutAsync(run.SnapshotKey, snapshot.RawContent, snapshot.RawContentType, ct);
            await db.SaveChangesAsync(ct);

            await EnsureSchemaUnchangedAsync(db, source, snapshot, acceptSchemaChange, ct);

            var context = new UpsertContext(run.Id, source.Id, assetType.Id, run.StartedAt, hadAssets);
            for (var offset = 0; offset < snapshot.Assets.Count; offset += BatchSize)
            {
                var batch = snapshot.Assets.Skip(offset).Take(BatchSize).ToList();
                var outcome = await upserter.UpsertAsync(db, context, batch, ct);
                run.AssetsCreated += outcome.Created;
                run.AssetsUpdated += outcome.Updated;
            }

            var seen = snapshot.Assets.Count;
            if (seen == 0)
                throw new InvalidOperationException("Source returned no assets; refusing to mark existing ones as removed.");
            if (previousActive > 0 && seen < previousActive * MinShareOfPreviousCount)
                throw new InvalidOperationException(
                    $"Source returned {seen} assets but {previousActive} were active before; refusing to mark the rest as removed. Check the source.");

            // Assets that vanished stay in the DB (quests reference them) but are flagged; their last known version
            // is recorded as a "removed" snapshot.
            run.AssetsRemoved = await MarkRemovedAsync(db, source.Id, run, ct);

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

    /// <summary>Fails the run if the source now delivers different fields than in the last successful run.</summary>
    private static async Task EnsureSchemaUnchangedAsync(AppDbContext db, DataSource source, SourceSnapshot snapshot, bool accept, CancellationToken ct)
    {
        var previous = await db.SyncRuns.AsNoTracking()
            .Where(r => r.DataSourceId == source.Id && r.Status == RunStatus.Succeeded && r.SchemaHash != null)
            .OrderByDescending(r => r.StartedAt).Select(r => r.SchemaHash).FirstOrDefaultAsync(ct);
        if (previous is null || previous == snapshot.SchemaHash || accept) return;
        throw new InvalidOperationException(
            $"The fields delivered by the source changed (now: {string.Join(", ", snapshot.SourceFields)}). " +
            "Check that the adapter still maps them correctly, then start the sync with acceptSchemaChange=true. " +
            "The download of this run was kept for inspection.");
    }

    private async Task<int> MarkRemovedAsync(AppDbContext db, Guid dataSourceId, SyncRun run, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var startedAt = run.StartedAt;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO asset_snapshot (id, asset_id, sync_run_id, change_type, geom, raw, source_hash)
            SELECT gen_random_uuid(), a.id, {run.Id}, 'removed', a.geom, a.raw, a.source_hash
            FROM asset a
            WHERE a.data_source_id = {dataSourceId} AND a.status = 'active' AND a.last_seen_at < {startedAt}
            """, ct);
        var removed = await db.Assets
            .Where(a => a.DataSourceId == dataSourceId && a.Status == AssetStatus.Active && a.LastSeenAt < startedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AssetStatus.RemovedAtSource)
                                      .SetProperty(a => a.UpdatedAt, clock.GetUtcNow()), ct);
        await tx.CommitAsync(ct);
        return removed;
    }
}
