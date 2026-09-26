using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Api.Publishing;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Tests;

/// <summary>
/// ADR-0014: nothing is pushed to open data (the city does not take updates that way), and it stays clear which data the city delivered and which
/// came from players. Every test works in its own patch of the map.
/// </summary>
[Collection(ApiCollection.Name)]
public class OpenDataTests(ApiFactory api)
{
    private const double Lon = 11.0;

    private static async Task<JsonElement> Body(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient player, Guid claimId, double lat, double lon, object payload)
        => player.PostAsync($"/claims/{claimId}/submit", new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" }, { new StringContent(lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(payload)), "payload" },
        });

    /// <summary>A genus quest for the tree; the player answers <paramref name="value"/>; returns the submission (pending).</summary>
    private async Task<Guid> SubmitGenusAsync(HttpClient admin, HttpClient player, Guid tree, double lat, string value, string attribute = "genus", string? unit = null)
    {
        // another unit makes another quest for the same tree and attribute (an open quest of the same kind would block it)
        var questId = await api.CreateQuestAsync(admin, tree, taskConfig: unit is null ? new { attribute } : new { attribute, unit });
        var claim = (await Body(await player.PostAsync($"/quests/{questId}/claim", null))).GetProperty("id").GetGuid();
        var res = await SubmitAsync(player, claim, lat, Lon, new { value });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await Body(res)).GetProperty("submissionId").GetGuid();
    }

    private async Task ApproveAsync(HttpClient admin, Guid submissionId)
    {
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        await WaitAsync(() => api.WithDb(db => db.OutboxMessages.AnyAsync(m => m.Type == nameof(AttributeChangeAccepted) && m.Status == OutboxStatus.Processed
            && EF.Functions.JsonContains(m.Payload, "{\"contribution\":{\"submissionId\":\"" + submissionId + "\"}}"))), "The change was not delivered.");
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException(message + " (within 20 s)");
    }

    private Task<Guid> TreeWithGenusAsync(double lat, string? genus) => api.AddTreeAsync(lat, Lon, genus);

    // ---- publishing is off ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Publishing_to_open_data_is_off_unless_somebody_switches_it_on()
    {
        Assert.False(new PublishingOptions().Enabled);
        Assert.False(new ConfigPublishingGate(Microsoft.Extensions.Options.Options.Create(new PublishingOptions())).Enabled);
        Assert.True(new ConfigPublishingGate(Microsoft.Extensions.Options.Options.Create(new PublishingOptions { Enabled = true })).Enabled);
    }

    [Fact]
    public async Task While_publishing_is_off_an_approved_change_stays_in_our_database_and_nothing_is_sent()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("keeper");
        var lat = 60.0;
        var tree = await TreeWithGenusAsync(lat, null);
        var published = api.Publisher.Batches.Count;
        api.PublishingGate.Enabled = false;
        try
        {
            var submission = await SubmitGenusAsync(admin, player, tree, lat, "Quercus");
            await ApproveAsync(admin, submission);

            var change = await api.WithDb(db => db.AttributeChanges.AsNoTracking().SingleAsync(c => c.SubmissionId == submission));
            Assert.Equal(ChangeStatus.Accepted, change.Status);   // accepted, not exported
            Assert.Null(change.ExportRunId);
            Assert.Equal(published, api.Publisher.Batches.Count);
            var feed = await api.CreateClient().GetAsync(Feed.Url);
            Assert.DoesNotContain(submission.ToString(), feed.StatusCode == HttpStatusCode.OK ? await feed.Content.ReadAsStringAsync() : "");

            // the game still knows it, as user data
            var detail = await Body(await player.GetAsync($"/assets/{tree}"));
            Assert.Equal("user", detail.GetProperty("effective").EnumerateArray().Single(e => e.GetProperty("attribute").GetString() == "genus").GetProperty("origin").GetString());

            // the admin can see what is kept, and retrying is refused while it is off
            var status = await Body(await admin.GetAsync("/admin/publications/status"));
            Assert.False(status.GetProperty("enabled").GetBoolean());
            Assert.True(status.GetProperty("acceptedNotPublished").GetInt32() >= 1);
            var retry = await admin.PostAsync("/admin/publications/retry", null);
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
            Assert.Equal("publishing_disabled", (await Body(retry)).GetProperty("error").GetString());

            // switched on later, the retry publishes what was accepted meanwhile
            api.PublishingGate.Enabled = true;
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/admin/publications/retry", null)).StatusCode);
            await Feed.WaitForChangesAsync(api, submission);
            await WaitAsync(async () => (await api.WithDb(db => db.AttributeChanges.AsNoTracking().SingleAsync(c => c.SubmissionId == submission))).Status == ChangeStatus.Exported, "Not exported.");
        }
        finally { api.PublishingGate.Enabled = true; }
    }

    [Fact]
    public async Task While_publishing_is_off_a_reported_tree_stays_a_user_tree_too()
    {
        var admin = await api.AdminAsync();
        var city = await admin.PostAsJsonAsync("/admin/cities", new { name = "Open data " + Guid.NewGuid().ToString("N")[..6] });
        var cityId = (await Body(city)).GetProperty("id").GetGuid();
        var lat = 60.5;
        var ring = new JsonArray(new JsonArray(Lon, lat), new JsonArray(Lon + 0.02, lat), new JsonArray(Lon + 0.02, lat + 0.02), new JsonArray(Lon, lat + 0.02), new JsonArray(Lon, lat));
        var districtId = (await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Mitte", geometry = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) } }))).GetProperty("id").GetGuid();
        var quest = await admin.PostAsJsonAsync("/admin/quests", new { taskType = "report_new_tree", maxCompletions = 3, rewardPoints = 10, taskConfig = new { dataSource = "de-muenster-trees" }, target = new { districtId } });
        var questId = (await Body(quest)).GetProperty("questIds")[0].GetGuid();
        var (player, username, _) = await api.RegisterAsync("finder");
        var claim = (await Body(await player.PostAsync($"/quests/{questId}/claim", null))).GetProperty("id").GetGuid();
        var form = new MultipartFormDataContent
        {
            { new StringContent((lat + 0.005).ToString(CultureInfo.InvariantCulture)), "lat" }, { new StringContent((Lon + 0.005).ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent("{\"genus\":\"Ginkgo\"}"), "payload" },
            { new ByteArrayContent(PhotoProcessorTests.MakeJpeg(320, 240, seed: 777)) { Headers = { ContentType = new("image/jpeg") } }, "photo", "photo.jpg" },
        };
        var submissionId = (await Body(await player.PostAsync($"/claims/{claim}/submit", form))).GetProperty("submissionId").GetGuid();
        var here = $"lat={(lat + 0.005).ToString(CultureInfo.InvariantCulture)}&lon={(Lon + 0.005).ToString(CultureInfo.InvariantCulture)}&radius=1000";

        api.PublishingGate.Enabled = false;
        try
        {
            // reported but not accepted yet: not shown
            Assert.Empty((await Body(await player.GetAsync($"/assets/reported?{here}"))).EnumerateArray());

            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
            await WaitAsync(() => api.WithDb(db => db.OutboxMessages.AnyAsync(m => m.Type == nameof(AttributeChangeAccepted) && m.Status == OutboxStatus.Processed
                && EF.Functions.JsonContains(m.Payload, "{\"contribution\":{\"submissionId\":\"" + submissionId + "\"}}"))), "The proposal was not delivered.");
            Assert.Equal(ChangeStatus.Accepted, (await api.WithDb(db => db.AssetProposals.AsNoTracking().SingleAsync(p => p.SubmissionId == submissionId))).Status);

            var trees = (await Body(await player.GetAsync($"/assets/reported?{here}"))).EnumerateArray().ToList();
            var tree = Assert.Single(trees);
            Assert.Equal(("user", "Ginkgo", username.ToLowerInvariant(), "de-muenster-trees"),
                (tree.GetProperty("origin").GetString(), tree.GetProperty("genus").GetString(), tree.GetProperty("username").GetString(), tree.GetProperty("dataSource").GetString()));
            Assert.Equal(lat + 0.005, tree.GetProperty("lat").GetDouble(), 6);
            // it is not an asset: the city's data does not have it
            Assert.DoesNotContain((await Body(await player.GetAsync($"/assets/nearby?{here}"))).EnumerateArray(), a => a.GetProperty("id").GetGuid() == tree.GetProperty("id").GetGuid());
            // and far away it is not listed
            Assert.Empty((await Body(await player.GetAsync($"/assets/reported?lat={(lat + 0.5).ToString(CultureInfo.InvariantCulture)}&lon={Lon.ToString(CultureInfo.InvariantCulture)}&radius=1000"))).EnumerateArray());
        }
        finally { api.PublishingGate.Enabled = true; }
        Assert.Equal(HttpStatusCode.BadRequest, (await player.GetAsync("/assets/reported?lat=99&lon=1")).StatusCode);
    }

    // ---- open data and user data ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_asset_shows_the_city_s_data_and_says_where_it_comes_from()
    {
        var (player, _, _) = await api.RegisterAsync("reader");
        var tree = await TreeWithGenusAsync(61.0, "Tilia");
        var detail = await Body(await player.GetAsync($"/assets/{tree}"));

        Assert.Equal("Tilia", detail.GetProperty("attributes").GetProperty("genus").GetString());
        Assert.Equal("de-muenster-trees", detail.GetProperty("source").GetProperty("key").GetString());
        Assert.False(string.IsNullOrEmpty(detail.GetProperty("source").GetProperty("attribution").GetString()));
        Assert.Empty(detail.GetProperty("contributions").EnumerateArray());
        Assert.All(detail.GetProperty("effective").EnumerateArray(), e => Assert.Equal("open_data", e.GetProperty("origin").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/assets/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/assets/{tree}")).StatusCode);
    }

    [Fact]
    public async Task A_player_s_accepted_value_is_told_apart_and_never_changes_the_city_s_attributes()
    {
        var admin = await api.AdminAsync();
        var (player, username, _) = await api.RegisterAsync("contributor");
        var lat = 61.1;
        var tree = await TreeWithGenusAsync(lat, null);
        var submission = await SubmitGenusAsync(admin, player, tree, lat, "Quercus");

        // pending: not a contribution yet
        Assert.Empty((await Body(await player.GetAsync($"/assets/{tree}"))).GetProperty("contributions").EnumerateArray());
        await ApproveAsync(admin, submission);

        var detail = await Body(await player.GetAsync($"/assets/{tree}"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("attributes").GetProperty("genus").ValueKind);   // the city's data is untouched
        var contribution = detail.GetProperty("contributions").EnumerateArray().Single();
        Assert.Equal(("genus", "Quercus", username.ToLowerInvariant(), false), (contribution.GetProperty("attribute").GetString(), contribution.GetProperty("value").GetString(),
            contribution.GetProperty("username").GetString(), contribution.GetProperty("outdated").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, contribution.GetProperty("previousValue").ValueKind);
        Assert.Equal(submission, contribution.GetProperty("submissionId").GetGuid());

        var effective = detail.GetProperty("effective").EnumerateArray().ToDictionary(e => e.GetProperty("attribute").GetString()!);
        Assert.Equal(("Quercus", "user", username.ToLowerInvariant()), (effective["genus"].GetProperty("value").GetString(), effective["genus"].GetProperty("origin").GetString(), effective["genus"].GetProperty("contributedBy").GetString()));
        Assert.Equal("open_data", effective["quality_flags"].GetProperty("origin").GetString());   // everything else is still the city's
    }

    [Fact]
    public async Task The_latest_contribution_of_an_attribute_wins_and_every_attribute_has_its_own()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("twice");
        var (other, otherName, _) = await api.RegisterAsync("later");
        var lat = 61.2;
        var tree = await TreeWithGenusAsync(lat, null);
        await ApproveAsync(admin, await SubmitGenusAsync(admin, player, tree, lat, "Quercus"));
        api.Clock.Advance(TimeSpan.FromHours(1));
        await ApproveAsync(admin, await SubmitGenusAsync(admin, other, tree, lat, "Fagus", unit: "again"));   // someone else corrects the genus
        await ApproveAsync(admin, await SubmitGenusAsync(admin, player, tree, lat, "Fagus sylvatica", attribute: "species"));   // and another attribute

        var contributions = (await Body(await player.GetAsync($"/assets/{tree}"))).GetProperty("contributions").EnumerateArray().ToDictionary(c => c.GetProperty("attribute").GetString()!);
        Assert.Equal(["genus", "species"], contributions.Keys.Order().ToArray());
        Assert.Equal(("Fagus", otherName.ToLowerInvariant()), (contributions["genus"].GetProperty("value").GetString(), contributions["genus"].GetProperty("username").GetString()));
        Assert.Equal("Fagus sylvatica", contributions["species"].GetProperty("value").GetString());
    }

    [Fact]
    public async Task When_the_city_delivers_the_same_value_later_it_is_open_data_again()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("confirmed");
        var lat = 61.3;
        var tree = await TreeWithGenusAsync(lat, null);
        await ApproveAsync(admin, await SubmitGenusAsync(admin, player, tree, lat, "Quercus"));
        await SetGenusAsync(tree, "Quercus");   // the next sync of the city has it

        var detail = await Body(await player.GetAsync($"/assets/{tree}"));
        var genus = detail.GetProperty("effective").EnumerateArray().Single(e => e.GetProperty("attribute").GetString() == "genus");
        Assert.Equal(("Quercus", "open_data"), (genus.GetProperty("value").GetString(), genus.GetProperty("origin").GetString()));
    }

    [Fact]
    public async Task When_the_city_changed_the_attribute_after_the_contribution_the_city_wins_and_the_contribution_is_outdated()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("outdated");
        var lat = 61.4;
        var tree = await TreeWithGenusAsync(lat, null);
        await ApproveAsync(admin, await SubmitGenusAsync(admin, player, tree, lat, "Quercus"));
        await SetGenusAsync(tree, "Acer");   // the city delivers something else

        var detail = await Body(await player.GetAsync($"/assets/{tree}"));
        var genus = detail.GetProperty("effective").EnumerateArray().Single(e => e.GetProperty("attribute").GetString() == "genus");
        Assert.Equal(("Acer", "open_data"), (genus.GetProperty("value").GetString(), genus.GetProperty("origin").GetString()));
        Assert.True(detail.GetProperty("contributions").EnumerateArray().Single().GetProperty("outdated").GetBoolean());
    }

    private Task SetGenusAsync(Guid assetId, string genus) => api.WithDb(async db =>
    {
        var attributes = JsonSerializer.Serialize(new { genus, quality_flags = Array.Empty<string>() });
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE asset SET attributes = {attributes}::jsonb WHERE id = {assetId}");
        return 0;
    });

    [Fact]
    public async Task The_lists_of_assets_and_quests_carry_the_origin_too()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("mapper");
        var lat = 61.5;
        var tree = await TreeWithGenusAsync(lat, null);
        await ApproveAsync(admin, await SubmitGenusAsync(admin, player, tree, lat, "Quercus"));
        var open = await api.CreateQuestAsync(admin, tree, "photo");   // a quest that is still open
        var near = $"lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={Lon.ToString(CultureInfo.InvariantCulture)}&radius=200";

        var asset = (await Body(await player.GetAsync($"/assets/nearby?{near}"))).EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == tree);
        Assert.Equal(("open_data", "de-muenster-trees"), (asset.GetProperty("origin").GetString(), asset.GetProperty("dataSource").GetString()));
        Assert.Equal("Quercus", asset.GetProperty("contributions").EnumerateArray().Single().GetProperty("value").GetString());

        var quest = (await Body(await player.GetAsync($"/quests/nearby?{near}"))).EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == open).GetProperty("asset");
        Assert.Equal("open_data", quest.GetProperty("origin").GetString());
        Assert.Equal("de-muenster-trees", quest.GetProperty("dataSource").GetString());
        Assert.Equal("genus", quest.GetProperty("contributions").EnumerateArray().Single().GetProperty("attribute").GetString());
        Assert.Equal(JsonValueKind.Null, quest.GetProperty("attributes").GetProperty("genus").ValueKind);   // the attributes are still only the city's
    }
}
