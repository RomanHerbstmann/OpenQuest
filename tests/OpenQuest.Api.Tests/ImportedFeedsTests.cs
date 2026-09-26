using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Data;

namespace OpenQuest.Api.Tests;

/// <summary>Reports and readings are written by the importer; the API reads them and derives quests from reports.</summary>
[Collection(ApiCollection.Name)]
public class ImportedFeedsTests(ApiFactory api)
{
    private static readonly GeometryFactory Wgs84 = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    private Task AddReportAsync(Guid? assetId, string category, string status, double lat, double lon) => api.WithDb(async db =>
    {
        var source = await db.DataSources.FirstAsync();
        var now = api.Clock.GetUtcNow();
        db.AssetReports.Add(new AssetReport
        {
            DataSourceId = source.Id, ExternalId = Guid.NewGuid().ToString("N"), Category = category, Status = status,
            Description = "Ast abgebrochen", Geom = Wgs84.CreatePoint(new Coordinate(lon, lat)), AssetId = assetId,
            DistanceM = assetId is null ? null : 3.5, ReportedAt = now.AddDays(-1), Raw = "{}", SourceHash = "x",
            FirstSeenAt = now, LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return 0;
    });

    [Fact]
    public async Task Quests_can_be_created_for_assets_with_an_open_report()
    {
        var admin = await api.AdminAsync();
        var damaged = await api.AddTreeAsync(51.9101, 7.6101);
        var closedOnly = await api.AddTreeAsync(51.9102, 7.6102);
        var mothOnly = await api.AddTreeAsync(51.9103, 7.6103);
        await AddReportAsync(damaged, "tree_damage", ReportStatus.Open, 51.9101, 7.6101);
        await AddReportAsync(closedOnly, "tree_damage", ReportStatus.Closed, 51.9102, 7.6102);
        await AddReportAsync(mothOnly, "oak_processionary_moth", ReportStatus.Open, 51.9103, 7.6103);

        var r = await admin.PostAsJsonAsync("/admin/quests", new
        {
            taskType = "condition_report", maxCompletions = 2, rewardPoints = 20,
            target = new { withOpenReport = "tree_damage" },
        });
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, body);
        var created = JsonDocument.Parse(body).RootElement;
        var questIds = created.GetProperty("questIds").EnumerateArray().Select(q => q.GetGuid()).ToList();

        var targeted = await api.WithDb(db => db.Quests.Where(q => questIds.Contains(q.Id)).Select(q => q.AssetId).ToListAsync());
        Assert.Contains(damaged, targeted);
        Assert.DoesNotContain(closedOnly, targeted);
        Assert.DoesNotContain(mothOnly, targeted);

        var any = await admin.PostAsJsonAsync("/admin/quests", new
        {
            taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { withOpenReport = "any" },
        });
        var anyIds = JsonDocument.Parse(await any.Content.ReadAsStringAsync()).RootElement
            .GetProperty("questIds").EnumerateArray().Select(q => q.GetGuid()).ToList();
        var anyTargets = await api.WithDb(db => db.Quests.Where(q => anyIds.Contains(q.Id)).Select(q => q.AssetId).ToListAsync());
        Assert.Contains(damaged, anyTargets);
        Assert.Contains(mothOnly, anyTargets);
        Assert.DoesNotContain(closedOnly, anyTargets);
    }

    [Fact]
    public async Task Reports_and_readings_are_listed_for_admins()
    {
        var admin = await api.AdminAsync();
        var tree = await api.AddTreeAsync(51.9201, 7.6201);
        await AddReportAsync(tree, "tree_damage", ReportStatus.Open, 51.9201, 7.6201);
        await api.WithDb(async db =>
        {
            var source = await db.DataSources.FirstAsync();
            db.EnvironmentReadings.Add(new EnvironmentReading
            {
                DataSourceId = source.Id, StationId = "1766", Metric = "soil_moisture_grass_sand_0_60cm", Value = 28,
                Unit = "%nFK", MeasuredAt = api.Clock.GetUtcNow().AddDays(-1), ImportedAt = api.Clock.GetUtcNow(),
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var reports = await admin.GetFromJsonAsync<JsonElement>("/admin/reports?status=open&category=tree_damage");
        Assert.Contains(reports.EnumerateArray(), r => r.GetProperty("assetId").GetString() == tree.ToString());

        var readings = await admin.GetFromJsonAsync<JsonElement>("/admin/readings?metric=soil_moisture_grass_sand_0_60cm&days=7");
        Assert.Contains(readings.EnumerateArray(), r => r.GetProperty("value").GetDouble() == 28);

        var (player, _, _) = await api.RegisterAsync("curious");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await player.GetAsync("/admin/reports")).StatusCode);
    }
}
