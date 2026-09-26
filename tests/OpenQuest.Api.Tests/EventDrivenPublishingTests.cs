using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Tests;

[Collection(ApiCollection.Name)]
public class EventDrivenPublishingTests(ApiFactory api)
{
    private const double Lat = 51.9, Lon = 7.5;

    private static async Task<Guid> SubmitGenusAsync(ApiFactory api, HttpClient admin, HttpClient player, double lat, double lon, string genus)
    {
        var assetId = await api.AddTreeAsync(lat, lon);
        var questId = await api.CreateQuestAsync(admin, assetId);
        var claim = await (await player.PostAsync($"/quests/{questId}/claim", null)).Content.ReadFromJsonAsync<JsonElement>();
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(System.Globalization.CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(lon.ToString(System.Globalization.CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(new { value = genus })), "payload" },
        };
        var res = await player.PostAsync($"/claims/{claim.GetProperty("id").GetGuid()}/submit", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionId").GetGuid();
    }

    [Fact]
    public async Task Approval_pushes_the_change_to_the_feed_within_seconds_without_any_manual_step()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("eventer");
        var submission = await SubmitGenusAsync(api, admin, player, Lat, Lon, "Quercus");

        var before = await api.CreateClient().GetAsync(Feed.Url);
        Assert.DoesNotContain(submission.ToString(), before.StatusCode == HttpStatusCode.OK ? await before.Content.ReadAsStringAsync() : "");

        var clock = Stopwatch.StartNew();
        await admin.PostAsJsonAsync($"/admin/submissions/{submission}/review", new { approved = true });
        await Feed.WaitForChangesAsync(api, submission);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");

        // the change is marked exported and recorded as a system-triggered publication
        var (status, runs, dead) = await api.WithDb(async db => (
            (await db.AttributeChanges.FirstAsync(c => c.SubmissionId == submission)).Status,
            await db.ExportRuns.CountAsync(r => r.CreatedBy == null && r.ChangeCount > 0),
            await db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Dead)));
        Assert.Equal(ChangeStatus.Exported, status);
        Assert.True(runs >= 1);
        Assert.Equal(0, dead);
    }

    [Fact]
    public async Task A_failing_publisher_is_retried_with_backoff_until_it_succeeds()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("retrier");
        var submission = await SubmitGenusAsync(api, admin, player, Lat + 0.01, Lon, "Fagus");

        var attemptsBefore = api.Publisher.Attempts;
        api.Publisher.FailNext(2);
        await admin.PostAsJsonAsync($"/admin/submissions/{submission}/review", new { approved = true });

        await Feed.WaitForChangesAsync(api, submission); // storage publisher runs before the failing one ... and again on retry
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && api.Publisher.Batches.All(b => b.NewChanges.All(c => c.SubmissionId != submission)))
            await Task.Delay(50);

        Assert.Contains(api.Publisher.Batches, b => b.NewChanges.Any(c => c.SubmissionId == submission));
        Assert.Equal(3, api.Publisher.Attempts - attemptsBefore); // 2 failures + 1 success

        var outbox = await api.WithDb(db => db.OutboxMessages.Where(m => m.Type == "AttributeChangeAccepted" && m.Attempts == 2).ToListAsync());
        Assert.NotEmpty(outbox);
        Assert.All(outbox, m => Assert.Equal(OutboxStatus.Processed, m.Status));
    }

    [Fact]
    public async Task Rejection_publishes_nothing_and_the_change_is_discarded()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("rejected");
        var submission = await SubmitGenusAsync(api, admin, player, Lat + 0.02, Lon, "Alnus");
        await admin.PostAsJsonAsync($"/admin/submissions/{submission}/review", new { approved = false, reason = "wrong" });

        await Task.Delay(500);
        var res = await api.CreateClient().GetAsync(Feed.Url);
        var body = res.StatusCode == HttpStatusCode.OK ? await res.Content.ReadAsStringAsync() : "";
        Assert.DoesNotContain(submission.ToString(), body);
        Assert.Equal(ChangeStatus.Discarded, await api.WithDb(async db => (await db.AttributeChanges.FirstAsync(c => c.SubmissionId == submission)).Status));
    }

    [Fact]
    public async Task Events_of_an_unhandled_type_are_delivered_and_recorded()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("rewarded");
        var submission = await SubmitGenusAsync(api, admin, player, Lat + 0.03, Lon, "Betula");
        await admin.PostAsJsonAsync($"/admin/submissions/{submission}/review", new { approved = true });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        int open;
        do
        {
            open = await api.WithDb(db => db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending));
            if (open == 0) break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < deadline);
        Assert.Equal(0, open);

        var messages = await api.WithDb(db => db.OutboxMessages.AsNoTracking().ToListAsync());
        var types = messages.Where(m => m.Payload.Contains(submission.ToString())).Select(m => m.Type).ToList();
        Assert.Contains("SubmissionApproved", types);   // future consumers (points, badges) subscribe to this
        Assert.Contains("AttributeChangeAccepted", types);
    }

    [Fact]
    public async Task Admin_can_see_outbox_status_and_republish_endpoint_is_admin_only()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("nosy");
        var status = await admin.GetFromJsonAsync<JsonElement>("/admin/outbox");
        Assert.True(status.TryGetProperty("pending", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsync("/admin/publications/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/admin/publications/retry", null)).StatusCode);
    }
}
