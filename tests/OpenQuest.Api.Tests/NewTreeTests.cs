using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Tests;

/// <summary>
/// Phase 5: trees that are missing in the data (quests of a district, reports that become proposals) and the problems a player can
/// report on a tree (<c>issues</c>). Every test works in its own patch of the map.
/// </summary>
[Collection(ApiCollection.Name)]
public class NewTreeTests(ApiFactory api)
{
    private const double Lon = 8.0;
    private const string DataSource = "de-muenster-trees";
    private static int _photoSeed = 1000;

    private sealed record Area(HttpClient Admin, Guid CityId, Guid DistrictId, double Lat);

    /// <summary>A city with one district: a 0.02° square from <paramref name="lat"/>/<see cref="Lon"/> up and to the east.</summary>
    private async Task<Area> NewAreaAsync(double lat, string? cityName = null)
    {
        var admin = await api.AdminAsync();
        var city = await admin.PostAsJsonAsync("/admin/cities", new { name = cityName ?? "New trees " + Guid.NewGuid().ToString("N")[..8] });
        var cityId = (await city.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var districtId = await AddDistrictAsync(admin, cityId, "Mitte", lat, Lon);
        return new Area(admin, cityId, districtId, lat);
    }

    private static async Task<Guid> AddDistrictAsync(HttpClient admin, Guid cityId, string name, double lat, double lon)
    {
        var ring = new JsonArray(new JsonArray(lon, lat), new JsonArray(lon + 0.02, lat), new JsonArray(lon + 0.02, lat + 0.02), new JsonArray(lon, lat + 0.02), new JsonArray(lon, lat));
        var geometry = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) };
        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name, geometry });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> CreateAreaQuest(HttpClient admin, object target, object? taskConfig = null, int maxCompletions = 3, int rewardPoints = 15, string? title = null)
        => admin.PostAsJsonAsync("/admin/quests", new
        {
            taskType = "report_new_tree", maxCompletions, rewardPoints, title, target,
            taskConfig = taskConfig ?? new { dataSource = DataSource },
        });

    private static async Task<Guid> QuestIdAsync(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("questIds")[0].GetGuid();
    }

    private static async Task<Guid> ClaimAsync(HttpClient player, Guid questId)
    {
        var res = await player.PostAsync($"/quests/{questId}/claim", null);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient player, Guid claimId, double lat, double lon, object payload, byte[]? photo = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(payload)), "payload" },
        };
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "photo", "photo.jpg");
        }
        return player.PostAsync($"/claims/{claimId}/submit", form);
    }

    private static byte[] Photo() => PhotoProcessorTests.MakeJpeg(320, 240, seed: Interlocked.Increment(ref _photoSeed));

    private static async Task<JsonElement> Body(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task WaitForAsync(Func<Task<bool>> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException(message + " (within 20 s)");
    }

    private Task WaitForApprovalEventsAsync(Guid submissionId) => WaitForAsync(() => api.WithDb(db => db.OutboxMessages.AnyAsync(m =>
        m.Type == nameof(SubmissionApproved) && m.Status == OutboxStatus.Processed
        && EF.Functions.JsonContains(m.Payload, "{\"submissionId\":\"" + submissionId + "\"}"))), "The approval was not delivered.");

    // ---- problems reported on a tree ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_condition_report_can_name_the_problems_and_they_are_published_as_a_second_change()
    {
        var admin = await api.AdminAsync();
        var lat = 40.0;
        var tree = await api.AddTreeAsync(lat, Lon);
        var questId = await api.CreateQuestAsync(admin, tree, taskType: "condition_report");
        var (player, _, _) = await api.RegisterAsync("inspector");
        var claimId = await ClaimAsync(player, questId);

        // only known problems, each once
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SubmitAsync(player, claimId, lat, Lon, new { condition = "damaged", issues = new[] { "on_fire" } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SubmitAsync(player, claimId, lat, Lon, new { condition = "damaged", issues = new[] { "fungus", "fungus" } })).StatusCode);

        var ok = await SubmitAsync(player, claimId, lat, Lon, new { condition = "damaged", issues = new[] { "root_lift", "fungus" }, note = "Wurzeln heben den Gehweg an" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var submissionId = (await Body(ok)).GetProperty("submissionId").GetGuid();

        var proposed = await api.WithDb(db => db.AttributeChanges.AsNoTracking().Where(c => c.SubmissionId == submissionId).OrderBy(c => c.AttributeKey).ToListAsync());
        Assert.Equal(["condition", "issues"], proposed.Select(c => c.AttributeKey).ToArray());
        Assert.Equal("\"damaged\"", proposed[0].NewValue);
        Assert.Equal(["root_lift", "fungus"], JsonSerializer.Deserialize<string[]>(proposed[1].NewValue!));

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        var feed = await Feed.WaitForChangesAsync(api, submissionId);
        var attributes = feed.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties"))
            .Where(p => p.GetProperty("submission_id").GetGuid() == submissionId).Select(p => p.GetProperty("attribute").GetString()).Order().ToArray();
        Assert.Equal(["condition", "issues"], attributes);
    }

    [Fact]
    public async Task A_condition_report_without_problems_still_works_as_before()
    {
        var admin = await api.AdminAsync();
        var lat = 40.1;
        var tree = await api.AddTreeAsync(lat, Lon);
        var questId = await api.CreateQuestAsync(admin, tree, taskType: "condition_report");
        var (player, _, _) = await api.RegisterAsync("plain");
        var res = await SubmitAsync(player, await ClaimAsync(player, questId), lat, Lon, new { condition = "good" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var submissionId = (await Body(res)).GetProperty("submissionId").GetGuid();
        Assert.Equal(["condition"], await api.WithDb(db => db.AttributeChanges.Where(c => c.SubmissionId == submissionId).Select(c => c.AttributeKey).ToListAsync()));
    }

    // ---- quests of a district ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_quest_to_report_new_trees_belongs_to_a_district_and_is_checked_when_created()
    {
        var area = await NewAreaAsync(40.2);
        var created = await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }, title: "Fehlt hier ein Baum?");
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await Body(created);
        Assert.Equal(1, body.GetProperty("created").GetInt32());
        var quest = await api.WithDb(db => db.Quests.AsNoTracking().SingleAsync(q => q.Id == body.GetProperty("questIds")[0].GetGuid()));
        Assert.Null(quest.AssetId);
        Assert.Equal(area.DistrictId, quest.DistrictId);
        Assert.Equal((3, 15, "Fehlt hier ein Baum?"), (quest.MaxCompletions, quest.RewardPoints, quest.Title));

        // the same quest is not created twice while it is open
        Assert.Equal(0, (await Body(await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }))).GetProperty("created").GetInt32());

        // the pieces are checked
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAreaQuest(area.Admin, new { limit = 3 })).StatusCode);                                                 // needs a district
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAreaQuest(area.Admin, new { districtId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }, new { dataSource = "nowhere" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }, new { })).StatusCode);                     // dataSource is required
    }

    [Fact]
    public async Task A_city_gets_one_quest_per_active_district()
    {
        var area = await NewAreaAsync(40.3);
        var second = await AddDistrictAsync(area.Admin, area.CityId, "Nord", 40.3 + 0.03, Lon);
        var inactive = await AddDistrictAsync(area.Admin, area.CityId, "Alt", 40.3 + 0.06, Lon);
        await area.Admin.PutAsJsonAsync($"/admin/districts/{inactive}", new { isActive = false });

        var res = await CreateAreaQuest(area.Admin, new { cityId = area.CityId });
        var ids = (await Body(res)).GetProperty("questIds").EnumerateArray().Select(q => q.GetGuid()).ToList();
        var districts = await api.WithDb(db => db.Quests.AsNoTracking().Where(q => ids.Contains(q.Id)).Select(q => q.DistrictId!.Value).ToListAsync());
        Assert.Equal(new[] { area.DistrictId, second }.Order(), districts.Order());
    }

    [Fact]
    public async Task Players_find_the_quest_of_the_district_they_stand_in_and_only_that()
    {
        var area = await NewAreaAsync(40.4);
        var questId = await QuestIdAsync(await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }));
        var (player, _, _) = await api.RegisterAsync("finder");

        var inside = await player.GetFromJsonAsync<JsonElement>($"/quests/areas?lat={(area.Lat + 0.01).ToString(CultureInfo.InvariantCulture)}&lon={(Lon + 0.01).ToString(CultureInfo.InvariantCulture)}");
        var quest = inside.EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == questId);
        Assert.Equal(JsonValueKind.Null, quest.GetProperty("asset").ValueKind);
        Assert.Equal("Mitte", quest.GetProperty("area").GetProperty("name").GetString());
        Assert.Equal(area.DistrictId, quest.GetProperty("area").GetProperty("districtId").GetGuid());
        Assert.Equal("report_new_tree", quest.GetProperty("taskType").GetString());

        var outside = await player.GetFromJsonAsync<JsonElement>($"/quests/areas?lat={(area.Lat + 0.5).ToString(CultureInfo.InvariantCulture)}&lon={Lon.ToString(CultureInfo.InvariantCulture)}");
        Assert.DoesNotContain(outside.EnumerateArray(), q => q.GetProperty("id").GetGuid() == questId);

        // the map of quests around a position only knows quests with an asset
        var nearby = await player.GetFromJsonAsync<JsonElement>($"/quests/nearby?lat={(area.Lat + 0.01).ToString(CultureInfo.InvariantCulture)}&lon={(Lon + 0.01).ToString(CultureInfo.InvariantCulture)}&radius=5000");
        Assert.DoesNotContain(nearby.EnumerateArray(), q => q.GetProperty("id").GetGuid() == questId);
        Assert.Equal(HttpStatusCode.BadRequest, (await player.GetAsync("/quests/areas?lat=99&lon=1")).StatusCode);

        // claimed: it disappears from the list and shows up under my claims, with the area
        await ClaimAsync(player, questId);
        var again = await player.GetFromJsonAsync<JsonElement>($"/quests/areas?lat={(area.Lat + 0.01).ToString(CultureInfo.InvariantCulture)}&lon={(Lon + 0.01).ToString(CultureInfo.InvariantCulture)}");
        Assert.DoesNotContain(again.EnumerateArray(), q => q.GetProperty("id").GetGuid() == questId);
        var mine = (await player.GetFromJsonAsync<JsonElement>("/me/claims")).EnumerateArray().Single(c => c.GetProperty("quest").GetProperty("id").GetGuid() == questId);
        Assert.Equal("Mitte", mine.GetProperty("quest").GetProperty("area").GetProperty("name").GetString());
    }

    // ---- reporting a tree ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_reported_tree_needs_a_photo_and_a_position_inside_the_district_and_no_known_tree_next_to_it()
    {
        var area = await NewAreaAsync(40.5);
        await api.AddTreeAsync(area.Lat + 0.010, Lon + 0.010);   // a tree that is in the data
        var questId = await QuestIdAsync(await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }, maxCompletions: 5));
        var (player, _, _) = await api.RegisterAsync("reporter");
        var claimId = await ClaimAsync(player, questId);
        var payload = new { genus = "Tilia", note = "steht seit Jahren hier" };

        Assert.Equal("invalid_submission", (await Body(await SubmitAsync(player, claimId, area.Lat + 0.005, Lon + 0.005, payload))).GetProperty("error").GetString());   // no photo
        var outside = await SubmitAsync(player, claimId, area.Lat + 0.5, Lon, payload, Photo());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outside.StatusCode);
        Assert.Equal("outside_area", (await Body(outside)).GetProperty("error").GetString());
        // 3 m from a tree that is in the data: not new
        var near = await SubmitAsync(player, claimId, area.Lat + 0.010 + 0.00003, Lon + 0.010, payload, Photo());
        Assert.Equal("tree_already_known", (await Body(near)).GetProperty("error").GetString());
        Assert.Equal(0, await api.WithDb(db => db.AssetProposals.CountAsync(p => p.DistrictId == area.DistrictId)));   // nothing was saved by the failed tries

        var ok = await SubmitAsync(player, claimId, area.Lat + 0.005, Lon + 0.005, payload, Photo());
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var submissionId = (await Body(ok)).GetProperty("submissionId").GetGuid();
        var proposal = await api.WithDb(db => db.AssetProposals.AsNoTracking().SingleAsync(p => p.SubmissionId == submissionId));
        Assert.Equal((ChangeStatus.Proposed, "Tilia", "steht seit Jahren hier", area.DistrictId), (proposal.Status, proposal.Genus, proposal.Note, proposal.DistrictId));
        Assert.Equal((area.Lat + 0.005, Lon + 0.005), (proposal.Geom.Y, proposal.Geom.X));
        Assert.StartsWith("/media/", proposal.PhotoUrl);
        Assert.Equal(0, await api.WithDb(db => db.AttributeChanges.CountAsync(c => c.SubmissionId == submissionId)));   // no asset, no attribute change

        // a second player reporting the same spot is told the tree was reported already
        var (other, _, _) = await api.RegisterAsync("second");
        var again = await SubmitAsync(other, await ClaimAsync(other, questId), area.Lat + 0.005 + 0.00002, Lon + 0.005, payload, Photo());
        Assert.Equal("tree_already_known", (await Body(again)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_approved_new_tree_is_published_with_the_other_changes_and_pays_points_to_the_district_but_gives_no_card()
    {
        var area = await NewAreaAsync(40.6);
        var questId = await QuestIdAsync(await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }, rewardPoints: 40));
        var (player, _, _) = await api.RegisterAsync("discoverer");
        var res = await SubmitAsync(player, await ClaimAsync(player, questId), area.Lat + 0.004, Lon + 0.004, new { genus = "Ginkgo", species = "Ginkgo biloba" }, Photo());
        var submissionId = (await Body(res)).GetProperty("submissionId").GetGuid();

        // the moderator sees it in the queue and the proposals list; the quest has no asset
        var queue = (await area.Admin.GetFromJsonAsync<JsonElement>("/admin/submissions")).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == submissionId);
        Assert.Equal(JsonValueKind.Null, queue.GetProperty("asset").ValueKind);
        Assert.Equal("Mitte", queue.GetProperty("area").GetProperty("name").GetString());
        var listed = (await area.Admin.GetFromJsonAsync<JsonElement>("/admin/proposals?status=proposed")).EnumerateArray().Single(p => p.GetProperty("submissionId").GetGuid() == submissionId);
        Assert.Equal("Ginkgo", listed.GetProperty("genus").GetString());
        Assert.Equal(DataSource, listed.GetProperty("dataSource").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/proposals")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await area.Admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        var feed = await Feed.WaitForChangesAsync(api, submissionId);
        var feature = feed.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("properties").GetProperty("submission_id").GetGuid() == submissionId);
        var props = feature.GetProperty("properties");
        Assert.Equal("new_tree", props.GetProperty("attribute").GetString());
        Assert.Equal("tree", props.GetProperty("asset_type").GetString());
        Assert.Equal("", props.GetProperty("external_id").GetString());
        Assert.Equal(JsonValueKind.Null, props.GetProperty("old_value").ValueKind);
        Assert.Equal("Ginkgo biloba", props.GetProperty("new_value").GetProperty("species").GetString());
        Assert.StartsWith("/media/", props.GetProperty("new_value").GetProperty("photo_url").GetString());
        Assert.Equal((Lon + 0.004, area.Lat + 0.004), (feature.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble(), feature.GetProperty("geometry").GetProperty("coordinates")[1].GetDouble()));

        await WaitForAsync(() => api.WithDb(async db => (await db.AssetProposals.AsNoTracking().SingleAsync(p => p.SubmissionId == submissionId)).Status == ChangeStatus.Exported), "The proposal was not exported.");
        await WaitForApprovalEventsAsync(submissionId);
        Assert.Equal(40, (await player.GetFromJsonAsync<JsonElement>("/me")).GetProperty("totalPoints").GetInt32());
        Assert.Equal(40, await api.WithDb(db => db.Districts.Where(d => d.Id == area.DistrictId).Select(d => d.TotalPoints).SingleAsync()));   // the points belong to the district of the quest
        Assert.Equal(0, await api.WithDb(db => db.Cards.CountAsync(c => c.SubmissionId == submissionId)));
    }

    [Fact]
    public async Task A_rejected_new_tree_is_discarded_frees_the_slot_and_lets_the_place_be_reported_again()
    {
        var area = await NewAreaAsync(40.7);
        var questId = await QuestIdAsync(await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }, maxCompletions: 1));
        var (player, _, _) = await api.RegisterAsync("wrong");
        var claimId = await ClaimAsync(player, questId);
        var res = await SubmitAsync(player, claimId, area.Lat + 0.004, Lon + 0.004, new { genus = "Tilia" }, Photo());
        var submissionId = (await Body(res)).GetProperty("submissionId").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await area.Admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = false, reason = "Das ist ein Busch" })).StatusCode);
        Assert.Equal(ChangeStatus.Discarded, (await api.WithDb(db => db.AssetProposals.AsNoTracking().SingleAsync(p => p.SubmissionId == submissionId))).Status);

        // the slot is free again and the discarded report no longer blocks the spot
        var (other, _, _) = await api.RegisterAsync("right");
        var retry = await SubmitAsync(other, await ClaimAsync(other, questId), area.Lat + 0.004, Lon + 0.004, new { genus = "Quercus" }, Photo());
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task Everything_else_still_works_for_quests_without_asset_in_the_lists_of_admins()
    {
        var area = await NewAreaAsync(40.8);
        var questId = await QuestIdAsync(await CreateAreaQuest(area.Admin, new { districtId = area.DistrictId }));
        var quests = await area.Admin.GetFromJsonAsync<JsonElement>("/admin/quests?limit=200");
        var quest = quests.EnumerateArray().Single(q => q.GetProperty("id").GetGuid() == questId);
        Assert.Equal(JsonValueKind.Null, quest.GetProperty("asset").ValueKind);
        Assert.Equal(area.DistrictId, quest.GetProperty("districtId").GetGuid());
        Assert.Equal("Mitte", quest.GetProperty("districtName").GetString());
        // a district with a quest cannot vanish behind its back: the quest keeps it
        Assert.Equal(HttpStatusCode.OK, (await area.Admin.PostAsJsonAsync($"/admin/quests/{questId}/status", new { status = "paused" })).StatusCode);
    }
}
