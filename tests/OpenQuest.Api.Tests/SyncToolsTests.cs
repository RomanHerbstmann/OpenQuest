using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenQuest.Api.Data;

namespace OpenQuest.Api.Tests;

/// <summary>Admin control over the importer: syncs on request (A4) and the download of a run's raw data (A3).</summary>
[Collection(ApiCollection.Name)]
public class SyncToolsTests(ApiFactory api)
{
    private static string Key() => "src-" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonElement> Body(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private Task FinishAllAsync() => api.WithDb(async db =>
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE sync_request SET status = 'succeeded', finished_at = now() WHERE status IN ('pending','running')");
        return 0;
    });

    // ---- sync on request ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_admin_can_ask_for_a_sync_and_the_importer_is_notified()
    {
        await FinishAllAsync();
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("nosy");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync("/admin/sync", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/sync/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().PostAsJsonAsync("/admin/sync", new { })).StatusCode);

        // what the importer does: listen on the channel
        var connectionString = api.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default")!;
        await using var listener = new NpgsqlConnection(connectionString);
        await listener.OpenAsync();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.Notification += (_, e) => received.TrySetResult(e.Payload);
        await using (var listen = new NpgsqlCommand("LISTEN sync_requested", listener)) await listen.ExecuteNonQueryAsync();
        _ = Task.Run(async () => { while (!received.Task.IsCompleted) await listener.WaitAsync(); });

        var res = await admin.PostAsJsonAsync("/admin/sync", new { });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var request = (await Body(res)).EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, request.GetProperty("dataSource").ValueKind);   // all enabled sources
        Assert.Equal(("pending", false, false, "admin"),
            (request.GetProperty("status").GetString(), request.GetProperty("force").GetBoolean(), request.GetProperty("acceptSchemaChange").GetBoolean(), request.GetProperty("requestedBy").GetString()));

        Assert.Equal(request.GetProperty("id").GetString(), await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        var stored = await api.WithDb(db => db.SyncRequests.AsNoTracking().SingleAsync(r => r.Id == request.GetProperty("id").GetGuid()));
        Assert.Equal(("*", SyncRequestStatus.Pending), (stored.DataSourceKey, stored.Status));
    }

    [Fact]
    public async Task Sources_are_named_by_key_and_each_gets_its_own_request_with_the_flags()
    {
        await FinishAllAsync();
        var admin = await api.AdminAsync();
        var (a, b) = (Key(), Key());
        var res = await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { a, b, a }, force = true, acceptSchemaChange = true });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var requests = (await Body(res)).EnumerateArray().ToList();
        Assert.Equal(new[] { a, b }.Order(), requests.Select(r => r.GetProperty("dataSource").GetString()!).Order());   // the duplicate is merged
        Assert.All(requests, r => Assert.True(r.GetProperty("force").GetBoolean() && r.GetProperty("acceptSchemaChange").GetBoolean()));

        var listed = (await admin.GetFromJsonAsync<JsonElement>("/admin/sync/requests")).EnumerateArray().ToList();
        Assert.Contains(listed, r => r.GetProperty("dataSource").GetString() == a && r.GetProperty("status").GetString() == "pending");
        Assert.Contains(listed, r => r.GetProperty("dataSource").GetString() == b);
    }

    [Fact]
    public async Task A_source_that_is_already_waiting_or_running_cannot_be_requested_again_until_it_is_done()
    {
        await FinishAllAsync();
        var admin = await api.AdminAsync();
        var key = Key();
        Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { key } })).StatusCode);
        var again = await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { key } });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("already_requested", (await Body(again)).GetProperty("error").GetString());
        // nothing was half saved: another source next to it is refused as a whole
        var other = Key();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { other, key } })).StatusCode);
        Assert.Equal(0, await api.WithDb(db => db.SyncRequests.CountAsync(r => r.DataSourceKey == other)));

        // running counts as well; done does not
        await api.WithDb(async db => { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sync_request SET status = 'running' WHERE data_source_key = {key}"); return 0; });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { key } })).StatusCode);
        await api.WithDb(async db => { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sync_request SET status = 'failed', error = 'x' WHERE data_source_key = {key}"); return 0; });
        Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { key } })).StatusCode);
    }

    [Fact]
    public async Task Source_keys_are_checked()
    {
        await FinishAllAsync();
        var admin = await api.AdminAsync();
        foreach (var sources in new[] { new[] { "Bad Key" }, new[] { "../etc" }, new[] { "" }, new[] { "*", "trees" }, Enumerable.Range(0, 21).Select(i => $"s{i}").ToArray() })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/sync", new { sources })).StatusCode);
        // "*" alone means all, like leaving the list out
        Assert.Equal(HttpStatusCode.Accepted, (await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { "*" } })).StatusCode);
    }

    [Fact]
    public async Task The_list_shows_what_the_importer_wrote_back()
    {
        await FinishAllAsync();
        var admin = await api.AdminAsync();
        var key = Key();
        var id = (await Body(await admin.PostAsJsonAsync("/admin/sync", new { sources = new[] { key } }))).EnumerateArray().Single().GetProperty("id").GetGuid();
        await api.WithDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE sync_request SET status = 'failed', started_at = now(), finished_at = now(), error = 'unknown source' WHERE id = {id}");
            return 0;
        });
        var request = (await admin.GetFromJsonAsync<JsonElement>("/admin/sync/requests?limit=100")).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal(("failed", "unknown source"), (request.GetProperty("status").GetString(), request.GetProperty("error").GetString()));
        Assert.NotEqual(JsonValueKind.Null, request.GetProperty("finishedAt").ValueKind);
    }

    // ---- raw download of a run ---------------------------------------------------------------------------------------------------------------

    private Task<Guid> AddRunAsync(string? snapshotKey) => api.WithDb(async db =>
    {
        var source = await db.DataSources.FirstAsync();
        var run = new SyncRun { DataSourceId = source.Id, StartedAt = new DateTimeOffset(2026, 9, 25, 4, 30, 15, TimeSpan.Zero), FinishedAt = api.Clock.GetUtcNow(), Status = RunStatus.Succeeded, SnapshotKey = snapshotKey };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    });

    [Fact]
    public async Task The_raw_download_of_a_run_comes_back_exactly_as_it_was_stored()
    {
        var admin = await api.AdminAsync();
        var key = $"de-muenster-trees/{Guid.NewGuid():N}.geojson";
        var raw = Encoding.UTF8.GetBytes("""{"type":"FeatureCollection","features":[]}""");
        await api.Storage.PutAsync(key, raw, "application/octet-stream");
        var runId = await AddRunAsync(key);

        var res = await admin.GetAsync($"/admin/sync/runs/{runId}/snapshot");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/geo+json", res.Content.Headers.ContentType!.MediaType);
        Assert.Equal("de-muenster-trees-20260925-043015.geojson", res.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal(raw, await res.Content.ReadAsByteArrayAsync());

        var (player, _, _) = await api.RegisterAsync("nosy");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync($"/admin/sync/runs/{runId}/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/admin/sync/runs/{runId}/snapshot")).StatusCode);
    }

    [Fact]
    public async Task A_run_with_reference_files_comes_back_as_one_archive()
    {
        var admin = await api.AdminAsync();
        var id = Guid.NewGuid().ToString("N");
        var (mainKey, streetsKey, areaKey) = ($"de-muenster-trees/{id}-m.geojson", $"de-muenster-trees/{id}-s.csv", $"de-muenster-trees/{id}-a.json");
        await api.Storage.PutAsync(mainKey, Encoding.UTF8.GetBytes("MAIN"), "x");
        await api.Storage.PutAsync(streetsKey, Encoding.UTF8.GetBytes("a,b"), "x");
        await api.Storage.PutAsync(areaKey, Encoding.UTF8.GetBytes("{}"), "x");
        var manifestKey = $"de-muenster-trees/{id}.manifest.json";
        await api.Storage.PutAsync(manifestKey, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            main = mainKey, extras = new Dictionary<string, string> { ["streets"] = streetsKey, ["enrichment:geo.area_name:2"] = areaKey },
        })), "x");
        var runId = await AddRunAsync(manifestKey);

        var res = await admin.GetAsync($"/admin/sync/runs/{runId}/snapshot");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/zip", res.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".zip", res.Content.Headers.ContentDisposition!.FileName);
        using var zip = new ZipArchive(new MemoryStream(await res.Content.ReadAsByteArrayAsync()));
        string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }
        var stem = "de-muenster-trees-20260925-043015";
        Assert.Equal(
            [$"{stem}/extras/enrichment_geo.area_name_2.json", $"{stem}/extras/streets.csv", $"{stem}/main.geojson"],
            zip.Entries.Select(e => e.FullName).Order().ToArray());   // names that are not file-safe are cleaned
        Assert.Equal("MAIN", Read($"{stem}/main.geojson"));
        Assert.Equal("a,b", Read($"{stem}/extras/streets.csv"));
    }

    [Fact]
    public async Task What_cannot_be_downloaded_is_explained()
    {
        var admin = await api.AdminAsync();
        Assert.Equal("run_not_found", (await Body(await admin.GetAsync($"/admin/sync/runs/{Guid.NewGuid()}/snapshot"))).GetProperty("error").GetString());

        var failed = await AddRunAsync(null);   // failed before it stored anything
        var none = await admin.GetAsync($"/admin/sync/runs/{failed}/snapshot");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("no_snapshot", (await Body(none)).GetProperty("error").GetString());

        var elsewhere = await AddRunAsync("de-muenster-trees/not-in-the-store.geojson");   // the importer keeps its files locally
        var missing = await admin.GetAsync($"/admin/sync/runs/{elsewhere}/snapshot");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("snapshot_unavailable", (await Body(missing)).GetProperty("error").GetString());
    }
}
