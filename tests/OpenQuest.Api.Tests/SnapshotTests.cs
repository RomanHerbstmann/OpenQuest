using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenQuest.Api.Data;
using OpenQuest.Api.Sync;
using OpenQuest.Core.Adapters;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Tests;

/// <summary>
/// Snapshots and history of the asset import (ERD: SYNC_RUN, ASSET_SNAPSHOT). Every test gets its own API and database,
/// because an import changes global state (baseline schema, which assets exist).
/// </summary>
public class SnapshotTests : IAsyncLifetime
{
    private readonly SyncFactory api = new();

    public Task InitializeAsync() => api.InitializeAsync();
    public Task DisposeAsync() => ((IAsyncLifetime)api).DisposeAsync();

    private const double Lat = 51.96, Lon = 7.62;

    private async Task<SyncRun> SyncAsync(SourceSnapshot next, bool acceptSchemaChange = false)
    {
        api.Adapter.Next = next;
        api.Clock.Advance(TimeSpan.FromMinutes(1)); // runs must be distinguishable in time (removal is decided by last-seen time)
        using var scope = api.Services.CreateScope();
        var runs = await scope.ServiceProvider.GetRequiredService<IAssetSynchronizer>().RunAsync(acceptSchemaChange, CancellationToken.None);
        return Assert.Single(runs!);
    }

    private static List<Asset> Trees(string prefix, int count, Func<int, string?>? genus = null)
        => Enumerable.Range(0, count).Select(i => ScriptedAdapter.Tree($"{prefix}{i}", Lat + i * 0.001, Lon, genus?.Invoke(i) ?? "Tilia")).ToList();

    private Task<List<AssetSnapshot>> SnapshotsOf(Guid runId)
        => api.WithDb(db => db.AssetSnapshots.AsNoTracking().Where(s => s.SyncRunId == runId).ToListAsync());

    [Fact]
    public async Task First_sync_records_the_download_and_a_created_snapshot_per_asset()
    {
        var download = ScriptedAdapter.Snapshot(Trees("first-", 10));
        var run = await SyncAsync(download);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal((10, 10), (run.AssetsCreated, run.RecordCount));
        Assert.Equal(download.SchemaHash, run.SchemaHash);
        Assert.NotNull(run.SnapshotKey);

        var snapshots = await SnapshotsOf(run.Id);
        Assert.Equal(10, snapshots.Count);
        Assert.All(snapshots, s => Assert.Equal(AssetChangeType.Created, s.ChangeType));

        // the raw download can be reloaded exactly as it was
        var admin = await api.AdminAsync();
        var file = await admin.GetAsync($"/admin/sync/runs/{run.Id}/snapshot");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(download.RawContent, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await (await api.RegisterAsync("peeker")).Client.GetAsync($"/admin/sync/runs/{run.Id}/snapshot")).StatusCode);
    }

    [Fact]
    public async Task An_unchanged_second_sync_adds_no_snapshot_rows()
    {
        var trees = Trees("same-", 10);
        await SyncAsync(ScriptedAdapter.Snapshot(trees));
        var second = await SyncAsync(ScriptedAdapter.Snapshot(trees));

        Assert.Equal((0, 0, 0), (second.AssetsCreated, second.AssetsUpdated, second.AssetsRemoved));
        Assert.Empty(await SnapshotsOf(second.Id));
        Assert.NotNull(second.SnapshotKey); // the download itself is still kept
    }

    [Fact]
    public async Task Changes_and_removals_are_recorded_and_the_history_can_be_read_back()
    {
        var trees = Trees("hist-", 10);
        await SyncAsync(ScriptedAdapter.Snapshot(trees));

        // tree 0: genus corrected by the city; tree 9: gone
        var changed = trees.Skip(1).Take(8).Prepend(ScriptedAdapter.Tree("hist-0", Lat, Lon, "Acer", "typo_corrected")).ToList();
        var second = await SyncAsync(ScriptedAdapter.Snapshot(changed));
        Assert.Equal((0, 1, 1), (second.AssetsCreated, second.AssetsUpdated, second.AssetsRemoved));

        var rows = await SnapshotsOf(second.Id);
        Assert.Equal(2, rows.Count);
        var updated = rows.Single(r => r.ChangeType == AssetChangeType.Updated);
        var removed = rows.Single(r => r.ChangeType == AssetChangeType.Removed);
        Assert.Contains("Acer", updated.Raw);      // the new version
        Assert.Contains("hist-9", removed.Raw);    // the last known version

        var (asset0, asset9) = await api.WithDb(async db => (
            await db.Assets.AsNoTracking().FirstAsync(a => a.ExternalId == "hist-0"),
            await db.Assets.AsNoTracking().FirstAsync(a => a.ExternalId == "hist-9")));
        Assert.Equal(AssetStatus.RemovedAtSource, asset9.Status);
        Assert.Equal(AssetStatus.Active, asset0.Status);

        var admin = await api.AdminAsync();
        var history = await admin.GetFromJsonAsync<JsonElement>($"/admin/assets/{asset0.Id}/history");
        Assert.Equal(new[] { "created", "updated" }, history.EnumerateArray().Select(h => h.GetProperty("changeType").GetString()!).ToArray());
        Assert.Equal("Tilia", history[0].GetProperty("raw").GetProperty("genus").GetString());
        Assert.Equal("Acer", history[1].GetProperty("raw").GetProperty("genus").GetString());
        Assert.Equal(new[] { "created", "removed" },
            (await admin.GetFromJsonAsync<JsonElement>($"/admin/assets/{asset9.Id}/history")).EnumerateArray().Select(h => h.GetProperty("changeType").GetString()!).ToArray());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/assets/{Guid.NewGuid()}/history")).StatusCode);
    }

    [Fact]
    public async Task A_tree_that_moved_slightly_keeps_its_identity_and_gets_an_updated_snapshot()
    {
        var trees = Trees("move-", 10);
        await SyncAsync(ScriptedAdapter.Snapshot(trees));
        var idBefore = await api.WithDb(async db => (await db.Assets.AsNoTracking().FirstAsync(a => a.ExternalId == "move-3")).Id);

        // the source now delivers tree 3 about 0.5 m further north, hence with a new derived id
        var moved = trees.Select(t => t.ExternalId == "move-3" ? ScriptedAdapter.Tree("move-3-shifted", Lat + 3 * 0.001 + 0.0000045, Lon, "Tilia") : t).ToList();
        var run = await SyncAsync(ScriptedAdapter.Snapshot(moved));

        Assert.Equal((0, 1, 0), (run.AssetsCreated, run.AssetsUpdated, run.AssetsRemoved));
        var rows = await SnapshotsOf(run.Id);
        Assert.Equal(idBefore, Assert.Single(rows).AssetId);
        Assert.Equal(AssetChangeType.Updated, rows[0].ChangeType);
    }

    [Fact]
    public async Task A_schema_change_fails_the_run_keeps_the_download_and_needs_explicit_acceptance()
    {
        var trees = Trees("schema-", 10);
        await SyncAsync(ScriptedAdapter.Snapshot(trees));

        var changedFields = ScriptedAdapter.Snapshot(trees.Select(t => ScriptedAdapter.Tree(t.ExternalId, t.Position.Lat, t.Position.Lon, "Quercus")).ToList(),
            fields: ["baumgruppe", "hoehe", "str_schl"]);
        var failed = await SyncAsync(changedFields);

        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Contains("fields delivered by the source changed", failed.Error);
        Assert.Equal(0, failed.AssetsUpdated);
        Assert.NotNull(failed.SnapshotKey); // evidence of what the source sent
        Assert.Empty(await SnapshotsOf(failed.Id));
        Assert.Equal(10, await api.WithDb(db => db.Assets.CountAsync(a => a.ExternalId.StartsWith("schema-") && EF.Functions.JsonContains(a.Attributes, "{\"genus\":\"Tilia\"}"))));

        var accepted = await SyncAsync(changedFields, acceptSchemaChange: true);
        Assert.Equal(RunStatus.Succeeded, accepted.Status);
        Assert.Equal(10, accepted.AssetsUpdated);

        // and the new field list is the baseline from now on
        Assert.Equal(RunStatus.Succeeded, (await SyncAsync(changedFields)).Status);
    }

    [Fact]
    public async Task A_source_that_suddenly_delivers_far_less_does_not_remove_anything()
    {
        var trees = Trees("mass-", 10);
        await SyncAsync(ScriptedAdapter.Snapshot(trees));
        var activeBefore = await api.WithDb(db => db.Assets.CountAsync(a => a.Status == AssetStatus.Active));

        var run = await SyncAsync(ScriptedAdapter.Snapshot(trees.Take(2).ToList()));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("refusing to mark the rest as removed", run.Error);
        Assert.Equal(activeBefore, await api.WithDb(db => db.Assets.CountAsync(a => a.Status == AssetStatus.Active)));
        Assert.DoesNotContain(await SnapshotsOf(run.Id), s => s.ChangeType == AssetChangeType.Removed);
    }
}
