using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Tests;

[Collection(ApiCollection.Name)]
public class PointsTests(ApiFactory api)
{
    private const double Lon = 7.55;

    /// <summary>Creates a quest with the given reward, lets the player complete it and returns the submission.</summary>
    private async Task<(Guid SubmissionId, Guid QuestId)> SubmitAsync(HttpClient admin, HttpClient player, double lat, int reward)
    {
        var assetId = await api.AddTreeAsync(lat, Lon);
        var questId = await api.CreateQuestAsync(admin, assetId, rewardPoints: reward);
        var claim = await (await player.PostAsync($"/quests/{questId}/claim", null)).Content.ReadFromJsonAsync<JsonElement>();
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(Lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(new { value = "Tilia" })), "payload" },
        };
        var res = await player.PostAsync($"/claims/{claim.GetProperty("id").GetGuid()}/submit", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return ((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionId").GetGuid(), questId);
    }

    private static async Task<JsonElement> Me(HttpClient player) => await player.GetFromJsonAsync<JsonElement>("/me");

    /// <summary>Points are paid by an event handler, so wait (max 15 s) until the player's total reaches the expected value.</summary>
    private static async Task<JsonElement> WaitForTotalAsync(HttpClient player, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        JsonElement me = default;
        while (DateTime.UtcNow < deadline)
        {
            me = await Me(player);
            if (me.GetProperty("totalPoints").GetInt32() == expected) return me;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"Total points did not reach {expected} within 15 s (last: {me}).");
    }

    /// <summary>Waits until the outbox has delivered every event of the given type for the submission.</summary>
    private async Task WaitForEventAsync(string type, Guid submissionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var done = await api.WithDb(db => db.OutboxMessages.AnyAsync(m =>
                m.Type == type && m.Status == OutboxStatus.Processed
                    && EF.Functions.JsonContains(m.Payload, "{\"submissionId\":\"" + submissionId + "\"}")));
            if (done) return;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"{type} for {submissionId} was not delivered within 15 s.");
    }

    [Fact]
    public async Task Approval_pays_the_reward_and_shows_points_level_and_ledger()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("scorer");
        var fresh = await Me(player);
        Assert.Equal(0, fresh.GetProperty("totalPoints").GetInt32());
        Assert.Equal(1, fresh.GetProperty("level").GetProperty("level").GetInt32());

        var (submissionId, questId) = await SubmitAsync(admin, player, 51.10, reward: 30);
        Assert.Equal(0, (await Me(player)).GetProperty("totalPoints").GetInt32()); // nothing before approval
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);

        var me = await WaitForTotalAsync(player, 30);
        var level = me.GetProperty("level");
        Assert.Equal(1, level.GetProperty("level").GetInt32());
        Assert.Equal(30, level.GetProperty("current").GetInt32());
        Assert.Equal(100, level.GetProperty("required").GetInt32());
        Assert.Equal(30, level.GetProperty("percent").GetInt32());
        Assert.False(level.GetProperty("isMaxLevel").GetBoolean());

        var ledger = (await player.GetFromJsonAsync<JsonElement>("/me/points")).EnumerateArray().ToList();
        var line = Assert.Single(ledger);
        Assert.Equal(30, line.GetProperty("amount").GetInt32());
        Assert.Equal("quest_approved", line.GetProperty("reason").GetString());
        Assert.Equal(submissionId, line.GetProperty("submissionId").GetGuid());
        Assert.False(string.IsNullOrEmpty(line.GetProperty("questTitle").GetString()));
        _ = questId;
    }

    [Fact]
    public async Task Points_add_up_and_the_player_levels_up()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("climber");
        var first = await SubmitAsync(admin, player, 51.11, reward: 60);
        var second = await SubmitAsync(admin, player, 51.12, reward: 60);
        foreach (var s in new[] { first.SubmissionId, second.SubmissionId })
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{s}/review", new { approved = true })).StatusCode);

        var me = await WaitForTotalAsync(player, 120);
        var level = me.GetProperty("level");
        Assert.Equal(2, level.GetProperty("level").GetInt32());
        Assert.Equal(20, level.GetProperty("current").GetInt32());
        Assert.Equal(150, level.GetProperty("required").GetInt32());
        Assert.Equal(13, level.GetProperty("percent").GetInt32());
        Assert.Equal(2, (await player.GetFromJsonAsync<JsonElement>("/me/points")).GetArrayLength());
    }

    [Fact]
    public async Task Rejection_and_zero_reward_pay_nothing()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("unlucky");

        var rejected = await SubmitAsync(admin, player, 51.13, reward: 40);
        await admin.PostAsJsonAsync($"/admin/submissions/{rejected.SubmissionId}/review", new { approved = false, reason = "blurry" });
        await WaitForEventAsync(nameof(SubmissionRejected), rejected.SubmissionId);

        var free = await SubmitAsync(admin, player, 51.14, reward: 0);
        await admin.PostAsJsonAsync($"/admin/submissions/{free.SubmissionId}/review", new { approved = true });
        await WaitForEventAsync(nameof(SubmissionApproved), free.SubmissionId);

        Assert.Equal(0, (await Me(player)).GetProperty("totalPoints").GetInt32());
        Assert.Equal(0, (await player.GetFromJsonAsync<JsonElement>("/me/points")).GetArrayLength());
    }

    [Fact]
    public async Task A_redelivered_event_does_not_pay_twice()
    {
        var admin = await api.AdminAsync();
        var (player, username, _) = await api.RegisterAsync("twice");
        var (submissionId, questId) = await SubmitAsync(admin, player, 51.15, reward: 25);
        await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true });
        await WaitForTotalAsync(player, 25);

        var userId = await api.WithDb(db => db.Users.Where(u => u.Username == username).Select(u => u.Id).FirstAsync());
        var again = new SubmissionApproved(submissionId, userId, questId, 25);
        using var scope = api.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IEventHandler<SubmissionApproved>>();
        await handler.HandleAsync([again], CancellationToken.None);          // redelivery of the same event
        await handler.HandleAsync([again, again], CancellationToken.None);   // and twice within one batch

        Assert.Equal(25, (await Me(player)).GetProperty("totalPoints").GetInt32());
        Assert.Equal(1, (await player.GetFromJsonAsync<JsonElement>("/me/points")).GetArrayLength());
        var (lines, cached, summed) = await api.WithDb(async db => (
            await db.PointTransactions.CountAsync(p => p.UserId == userId),
            await db.Users.Where(u => u.Id == userId).Select(u => u.TotalPoints).FirstAsync(),
            await db.PointTransactions.Where(p => p.UserId == userId).SumAsync(p => p.Amount)));
        Assert.Equal((1, 25, 25), (lines, cached, summed));
    }

    [Fact]
    public async Task Points_endpoints_need_a_login_and_only_show_the_own_ledger()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/me/points")).StatusCode);

        var admin = await api.AdminAsync();
        var (owner, _, _) = await api.RegisterAsync("owner");
        var (other, _, _) = await api.RegisterAsync("bystander");
        var (submissionId, _) = await SubmitAsync(admin, owner, 51.16, reward: 10);
        await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true });
        await WaitForTotalAsync(owner, 10);

        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/me/points")).GetArrayLength());
        Assert.Equal(0, (await Me(other)).GetProperty("totalPoints").GetInt32());
        // paging parameters are clamped instead of failing
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/me/points?offset=-5&limit=1000")).StatusCode);
    }
}
