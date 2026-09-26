using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Tests;

/// <summary>
/// The importer writes sync runs and asset history; the API only shows them (admin, read only). The tests write the rows
/// the way the importer does and check what the admin endpoints return.
/// </summary>
[Collection(ApiCollection.Name)]
public class ImportedDataTests(ApiFactory api)
{
    private static readonly GeometryFactory Wgs84 = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
    private static readonly DateTimeOffset Day1 = new(2026, 9, 20, 3, 0, 0, TimeSpan.Zero);

    private static Point At(double lat, double lon) => Wgs84.CreatePoint(new Coordinate(lon, lat));

    private async Task<SyncRun> AddRunAsync(DateTimeOffset startedAt, string status, int records = 0, int created = 0,
        int updated = 0, int removed = 0, string? snapshotKey = null, string? error = null) => await api.WithDb(async db =>
    {
        var source = await db.DataSources.FirstAsync();
        var run = new SyncRun
        {
            DataSourceId = source.Id, StartedAt = startedAt, FinishedAt = startedAt.AddMinutes(2), Status = status,
            SnapshotKey = snapshotKey, SchemaHash = "hash-" + Guid.NewGuid().ToString("N")[..8], RecordCount = records,
            AssetsCreated = created, AssetsUpdated = updated, AssetsRemoved = removed, Error = error,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    });

    private Task AddSnapshotAsync(Guid assetId, Guid runId, AssetChangeType type, double lat, double lon, string rawJson) =>
        api.WithDb(async db =>
        {
            db.AssetSnapshots.Add(new AssetSnapshot
            {
                AssetId = assetId, SyncRunId = runId, ChangeType = type, Geom = At(lat, lon), Raw = rawJson,
                SourceHash = "src-" + Guid.NewGuid().ToString("N")[..8],
            });
            await db.SaveChangesAsync();
            return 0;
        });

    [Fact]
    public async Task Sync_runs_are_listed_newest_first_with_counts_and_errors()
    {
        var ok = await AddRunAsync(Day1, RunStatus.Succeeded, records: 43_114, created: 43_114, snapshotKey: "de-muenster-trees/abc.geojson");
        var failed = await AddRunAsync(Day1.AddDays(1), RunStatus.Failed, error: "SchemaChangedError: Source fields changed");
        var admin = await api.AdminAsync();

        var runs = (await admin.GetFromJsonAsync<JsonElement>("/admin/sync/runs")).EnumerateArray().ToList();

        var ids = runs.Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.True(ids.IndexOf(failed.Id) >= 0 && ids.IndexOf(failed.Id) < ids.IndexOf(ok.Id), "newest run comes first");

        var okRow = runs.Single(r => r.GetProperty("id").GetGuid() == ok.Id);
        Assert.Equal("de-muenster-trees", okRow.GetProperty("dataSource").GetString());
        Assert.Equal("succeeded", okRow.GetProperty("status").GetString());
        Assert.Equal(43_114, okRow.GetProperty("recordCount").GetInt32());
        Assert.Equal(43_114, okRow.GetProperty("assetsCreated").GetInt32());
        Assert.True(okRow.GetProperty("hasSnapshot").GetBoolean());
        Assert.Equal(JsonValueKind.Null, okRow.GetProperty("error").ValueKind);

        var failedRow = runs.Single(r => r.GetProperty("id").GetGuid() == failed.Id);
        Assert.Equal("failed", failedRow.GetProperty("status").GetString());
        Assert.False(failedRow.GetProperty("hasSnapshot").GetBoolean());
        Assert.Contains("Source fields changed", failedRow.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Asset_history_lists_the_versions_in_the_order_of_the_runs()
    {
        var assetId = await api.AddTreeAsync(51.9600, 7.6200, genus: null);
        var first = await AddRunAsync(Day1.AddDays(10), RunStatus.Succeeded, created: 1);
        var second = await AddRunAsync(Day1.AddDays(11), RunStatus.Succeeded, updated: 1);
        var third = await AddRunAsync(Day1.AddDays(12), RunStatus.Succeeded, removed: 1);
        // written out of order on purpose: the API sorts by the run's start
        await AddSnapshotAsync(assetId, third.Id, AssetChangeType.Removed, 51.96002, 7.62001, """{"properties":{"baumgruppe":"Tilia"}}""");
        await AddSnapshotAsync(assetId, first.Id, AssetChangeType.Created, 51.9600, 7.6200, """{"properties":{"baumgruppe":"Baum Amt62"}}""");
        await AddSnapshotAsync(assetId, second.Id, AssetChangeType.Updated, 51.96002, 7.62001, """{"properties":{"baumgruppe":"Tilia"}}""");
        var admin = await api.AdminAsync();

        var history = (await admin.GetFromJsonAsync<JsonElement>($"/admin/assets/{assetId}/history")).EnumerateArray().ToList();

        Assert.Equal(["created", "updated", "removed"], history.Select(h => h.GetProperty("changeType").GetString()));
        Assert.Equal([first.Id, second.Id, third.Id], history.Select(h => h.GetProperty("syncRunId").GetGuid()));
        Assert.Equal(51.9600, history[0].GetProperty("lat").GetDouble(), 6);
        Assert.Equal(7.6200, history[0].GetProperty("lon").GetDouble(), 6);
        Assert.Equal(51.96002, history[1].GetProperty("lat").GetDouble(), 6);
        Assert.Equal("Baum Amt62", history[0].GetProperty("raw").GetProperty("properties").GetProperty("baumgruppe").GetString());
        Assert.Equal("Tilia", history[1].GetProperty("raw").GetProperty("properties").GetProperty("baumgruppe").GetString());
        Assert.False(string.IsNullOrEmpty(history[0].GetProperty("sourceHash").GetString()));
    }

    [Fact]
    public async Task Asset_without_history_has_an_empty_list_and_an_unknown_asset_is_404()
    {
        var assetId = await api.AddTreeAsync(51.9610, 7.6210);
        var admin = await api.AdminAsync();

        var empty = await admin.GetFromJsonAsync<JsonElement>($"/admin/assets/{assetId}/history");
        Assert.Equal(JsonValueKind.Array, empty.ValueKind);
        Assert.Equal(0, empty.GetArrayLength());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/admin/assets/{Guid.NewGuid()}/history")).StatusCode);
    }

    [Fact]
    public async Task Sync_endpoints_are_admin_only()
    {
        var assetId = await api.AddTreeAsync(51.9620, 7.6220);
        var (player, _, _) = await api.RegisterAsync("nosync");
        var anonymous = api.CreateClient();

        foreach (var url in new[] { "/admin/sync/runs", $"/admin/assets/{assetId}/history" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync(url)).StatusCode);
        }
    }
}
