using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenQuest.Api.Tests;

[Collection(ApiCollection.Name)]
public class GameFlowTests(ApiFactory api)
{
    private const double Lat = 51.9625, Lon = 7.6256;

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> Submit(HttpClient c, Guid claimId, double lat, double lon, object payload, byte[]? photo = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(System.Globalization.CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(lon.ToString(System.Globalization.CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(payload)), "payload" },
        };
        if (photo is not null)
        {
            var file = new ByteArrayContent(photo);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "photo", "photo.jpg");
        }
        return c.PostAsync($"/claims/{claimId}/submit", form);
    }

    [Fact]
    public async Task Geofence_payload_validation_and_review_lifecycle()
    {
        var admin = await api.AdminAsync();
        var assetId = await api.AddTreeAsync(Lat, Lon);
        var questId = await api.CreateQuestAsync(admin, assetId, maxCompletions: 1);
        var (player, _, _) = await api.RegisterAsync("walker");

        var claim = await Json(await player.PostAsync($"/quests/{questId}/claim", null));
        var claimId = claim.GetProperty("id").GetGuid();

        // ~110 m away -> outside the 30 m geofence
        var far = await Submit(player, claimId, Lat + 0.001, Lon, new { value = "Tilia" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, far.StatusCode);
        Assert.Equal("outside_geofence", (await Json(far)).GetProperty("error").GetString());

        // schema violations
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Submit(player, claimId, Lat, Lon, new { value = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Submit(player, claimId, Lat, Lon, new { value = "Tilia", extra = 1 })).StatusCode);

        var ok = await Submit(player, claimId, Lat + 0.0001, Lon, new { value = "Tilia" });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var submissionId = (await Json(ok)).GetProperty("submissionId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(player, claimId, Lat, Lon, new { value = "Tilia" })).StatusCode); // already submitted

        // moderation: reject needs a reason; a player may not review
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = false })).StatusCode);

        // the pending submission holds the only slot
        var other = await api.RegisterAsync("other");
        Assert.Equal(HttpStatusCode.Conflict, (await other.Client.PostAsync($"/quests/{questId}/claim", null)).StatusCode);

        var rejected = await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = false, reason = "blurry" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await other.Client.PostAsync($"/quests/{questId}/claim", null)).StatusCode); // slot freed
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
    }

    [Fact]
    public async Task Approved_submission_reaches_the_public_feed_and_photo_is_public_only_after_approval()
    {
        var admin = await api.AdminAsync();
        var assetId = await api.AddTreeAsync(Lat + 0.01, Lon + 0.01);
        var photoQuest = await api.CreateQuestAsync(admin, assetId, taskType: "photo");
        var genusQuest = await api.CreateQuestAsync(admin, assetId, taskType: "verify_attribute");
        var (player, _, _) = await api.RegisterAsync("shooter");
        var (la, lo) = (Lat + 0.01, Lon + 0.01);

        // photo
        var photoClaim = (await Json(await player.PostAsync($"/quests/{photoQuest}/claim", null))).GetProperty("id").GetGuid();
        var jpeg = PhotoProcessorTests.MakeJpeg(640, 480, seed: 4242, exifOrientation: 1, comment: "SecretCamera");
        var res = await Submit(player, photoClaim, la, lo, new { }, jpeg);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await Json(res);
        var mediaId = body.GetProperty("mediaId").GetGuid();
        var photoSubmission = body.GetProperty("submissionId").GetGuid();

        var stored = api.Storage.Objects[$"submissions/{photoSubmission}.jpg"];
        Assert.DoesNotContain("SecretCamera", System.Text.Encoding.Latin1.GetString(stored));

        Assert.Equal(HttpStatusCode.NotFound, (await api.CreateClient().GetAsync($"/media/{mediaId}")).StatusCode); // not public yet
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/admin/media/{mediaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync($"/admin/media/{mediaId}")).StatusCode);
        await admin.PostAsJsonAsync($"/admin/submissions/{photoSubmission}/review", new { approved = true });
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync($"/media/{mediaId}")).StatusCode);

        // the same photo again on another tree is a duplicate
        var assetB = await api.AddTreeAsync(Lat + 0.02, Lon + 0.02);
        var questB = await api.CreateQuestAsync(admin, assetB, taskType: "photo");
        var claimB = (await Json(await player.PostAsync($"/quests/{questB}/claim", null))).GetProperty("id").GetGuid();
        var dup = await Submit(player, claimB, Lat + 0.02, Lon + 0.02, new { }, jpeg);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("duplicate_photo", (await Json(dup)).GetProperty("error").GetString());

        // genus
        var genusClaim = (await Json(await player.PostAsync($"/quests/{genusQuest}/claim", null))).GetProperty("id").GetGuid();
        var g = await Submit(player, genusClaim, la, lo, new { value = "Acer" });
        var genusSubmission = (await Json(g)).GetProperty("submissionId").GetGuid();
        await admin.PostAsJsonAsync($"/admin/submissions/{genusSubmission}/review", new { approved = true });

        // no export step: the accepted changes reach the public feed by themselves (event-driven)
        var fc = await Feed.WaitForChangesAsync(api, genusSubmission, photoSubmission);
        Assert.Contains("Stadt Münster", fc.GetProperty("attribution").GetString());
        var changes = fc.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties")).ToList();
        var genus = changes.Single(c => c.GetProperty("attribute").GetString() == "genus" && c.GetProperty("submission_id").GetGuid() == genusSubmission);
        Assert.Equal("Acer", genus.GetProperty("new_value").GetString());
        Assert.Equal(JsonValueKind.Null, genus.GetProperty("old_value").ValueKind);
        var photo = changes.Single(c => c.GetProperty("submission_id").GetGuid() == photoSubmission);
        Assert.Equal($"/media/{mediaId}", photo.GetProperty("new_value").GetString());
    }

    [Fact]
    public async Task Nearby_quests_are_sorted_by_distance_and_hide_taken_ones()
    {
        var admin = await api.AdminAsync();
        double baseLat = 50.5, baseLon = 8.5; // isolated spot so other tests' quests don't interfere
        var near = await api.AddTreeAsync(baseLat, baseLon);
        var mid = await api.AddTreeAsync(baseLat + 0.001, baseLon);
        var far = await api.AddTreeAsync(baseLat + 0.01, baseLon); // ~1.1 km
        var qNear = await api.CreateQuestAsync(admin, near);
        var qMid = await api.CreateQuestAsync(admin, mid);
        await api.CreateQuestAsync(admin, far);
        var (player, _, _) = await api.RegisterAsync("scout");

        var list = await player.GetFromJsonAsync<JsonElement>(FormattableString.Invariant($"/quests/nearby?lat={baseLat}&lon={baseLon}&radius=500"));
        Assert.Equal([qNear, qMid], list.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToArray());
        Assert.True(list[0].GetProperty("distanceMeters").GetDouble() < list[1].GetProperty("distanceMeters").GetDouble());

        await player.PostAsync($"/quests/{qNear}/claim", null);
        list = await player.GetFromJsonAsync<JsonElement>(FormattableString.Invariant($"/quests/nearby?lat={baseLat}&lon={baseLon}&radius=500"));
        Assert.Equal([qMid], list.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToArray());
    }
}
