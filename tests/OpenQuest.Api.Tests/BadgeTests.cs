using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenQuest.Api.Badges;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Tests;

/// <summary>Badges (ADR-0012): the catalog, earning them by what a player did, progress, bonus points, and what admins can change.</summary>
[Collection(ApiCollection.Name)]
public class BadgeTests(ApiFactory api)
{
    private const double Lon = 10.0;
    private static int _seed = 9000;

    private static string Key() => "b" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<JsonElement> Body(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>The player completes a quest (verify_attribute unless told otherwise) and a moderator approves it; waits until all handlers have run.</summary>
    private async Task<Guid> ApproveOneAsync(HttpClient admin, HttpClient player, double lat, string taskType = "verify_attribute", int reward = 10)
    {
        var tree = await api.AddTreeAsync(lat, Lon);
        var questId = await api.CreateQuestAsync(admin, tree, taskType, rewardPoints: reward);
        var claim = (await Body(await player.PostAsync($"/quests/{questId}/claim", null))).GetProperty("id").GetGuid();
        object payload = taskType == "condition_report" ? new { condition = "good" } : new { value = "Tilia" };
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" }, { new StringContent(Lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(payload)), "payload" },
        };
        var submitted = await player.PostAsync($"/claims/{claim}/submit", form);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var submissionId = (await Body(submitted)).GetProperty("submissionId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        await WaitAsync(() => api.WithDb(db => db.OutboxMessages.AnyAsync(m => m.Type == nameof(SubmissionApproved) && m.Status == OutboxStatus.Processed
            && EF.Functions.JsonContains(m.Payload, "{\"submissionId\":\"" + submissionId + "\"}"))), "The approval was not delivered.");
        return submissionId;
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

    private static async Task<JsonElement> BadgeOf(HttpClient player, string key)
        => (await player.GetFromJsonAsync<JsonElement>("/me/badges")).EnumerateArray().Single(b => b.GetProperty("key").GetString() == key);

    private static Task<HttpResponseMessage> CreateBadge(HttpClient admin, string key, object criteria, int reward = 0, bool? active = null)
        => admin.PostAsJsonAsync("/admin/badges", new { key, name = "Badge " + key, description = "Test", criteria, rewardPoints = reward, isActive = active });

    // ---- the catalog ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_default_catalog_is_there_for_logged_in_players_and_pays_no_points()
    {
        var (player, _, _) = await api.RegisterAsync("browser");
        var badges = (await player.GetFromJsonAsync<JsonElement>("/badges")).EnumerateArray().ToList();
        foreach (var def in BadgeCatalog.Defaults)
        {
            var badge = badges.Single(b => b.GetProperty("key").GetString() == def.Key);
            Assert.Equal($"badge.{def.Key}", badge.GetProperty("name").GetString());
            Assert.Equal(def.Criteria.Type, badge.GetProperty("criteria").GetProperty("type").GetString());
            Assert.Equal(0, badge.GetProperty("rewardPoints").GetInt32());
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/badges")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/me/badges")).StatusCode);
    }

    [Fact]
    public async Task A_new_player_has_earned_nothing_and_sees_how_far_they_are()
    {
        var (player, _, _) = await api.RegisterAsync("newbie");
        var mine = (await player.GetFromJsonAsync<JsonElement>("/me/badges")).EnumerateArray().ToList();
        Assert.Equal(BadgeCatalog.Defaults.Count, mine.Count(b => !b.GetProperty("earned").GetBoolean()));
        var regular = mine.Single(b => b.GetProperty("key").GetString() == "regular");
        Assert.Equal((0, 10, 0), (regular.GetProperty("progress").GetProperty("current").GetInt32(), regular.GetProperty("progress").GetProperty("required").GetInt32(), regular.GetProperty("progress").GetProperty("percent").GetInt32()));
        Assert.Equal(JsonValueKind.Null, regular.GetProperty("awardedAt").ValueKind);
        Assert.Equal(0, (await player.GetFromJsonAsync<JsonElement>("/me")).GetProperty("badgeCount").GetInt32());
    }

    // ---- earning ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_first_approval_earns_the_first_badge_and_the_progress_of_the_others_moves()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("starter");
        await ApproveOneAsync(admin, player, 50.0);

        var first = await BadgeOf(player, "first_steps");
        Assert.True(first.GetProperty("earned").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("awardedAt").ValueKind);
        Assert.Equal(100, first.GetProperty("progress").GetProperty("percent").GetInt32());
        var regular = await BadgeOf(player, "regular");
        Assert.False(regular.GetProperty("earned").GetBoolean());
        Assert.Equal((1, 10, 10), (regular.GetProperty("progress").GetProperty("current").GetInt32(), regular.GetProperty("progress").GetProperty("required").GetInt32(), regular.GetProperty("progress").GetProperty("percent").GetInt32()));

        var mine = (await player.GetFromJsonAsync<JsonElement>("/me/badges")).EnumerateArray().ToList();
        Assert.True(mine[0].GetProperty("earned").GetBoolean());   // earned ones come first
        Assert.Equal(1, (await player.GetFromJsonAsync<JsonElement>("/me")).GetProperty("badgeCount").GetInt32());
        Assert.Equal(10, (await player.GetFromJsonAsync<JsonElement>("/me")).GetProperty("totalPoints").GetInt32());   // the default badges pay nothing
    }

    [Fact]
    public async Task A_badge_that_needs_two_approvals_is_earned_with_the_second_and_only_once()
    {
        var admin = await api.AdminAsync();
        var key = Key();
        Assert.Equal(HttpStatusCode.Created, (await CreateBadge(admin, key, new { type = "approved_submissions", count = 2 })).StatusCode);
        var (player, username, _) = await api.RegisterAsync("twice");
        var userId = await api.WithDb(db => db.Users.Where(u => u.Username == username.ToLowerInvariant()).Select(u => u.Id).SingleAsync());

        await ApproveOneAsync(admin, player, 50.1);
        Assert.False((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());
        await ApproveOneAsync(admin, player, 50.11);
        Assert.True((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());

        var held = await api.WithDb(db => db.UserBadges.CountAsync(u => u.UserId == userId && db.Badges.Any(b => b.Id == u.BadgeId && b.Key == key)));
        Assert.Equal(1, held);
    }

    [Fact]
    public async Task Evaluating_again_or_at_the_same_time_awards_nothing_twice()
    {
        var admin = await api.AdminAsync();
        var (player, username, _) = await api.RegisterAsync("racer");
        await ApproveOneAsync(admin, player, 50.2);
        var userId = await api.WithDb(db => db.Users.Where(u => u.Username == username.ToLowerInvariant()).Select(u => u.Id).SingleAsync());
        var before = await api.WithDb(db => db.UserBadges.CountAsync(u => u.UserId == userId));
        Assert.True(before >= 1);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = api.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IBadgeService>().EvaluateUserAsync(userId, CancellationToken.None);
        }));
        Assert.All(results, r => Assert.Empty(r));
        Assert.Equal(before, await api.WithDb(db => db.UserBadges.CountAsync(u => u.UserId == userId)));
    }

    [Fact]
    public async Task Approvals_of_one_task_type_earn_a_badge_for_that_task_type()
    {
        var admin = await api.AdminAsync();
        var key = Key();
        await CreateBadge(admin, key, new { type = "task_type", taskType = "condition_report", count = 1 });
        var (player, _, _) = await api.RegisterAsync("doctor");
        await ApproveOneAsync(admin, player, 50.3);   // a genus quest: not the right task type
        Assert.False((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());
        await ApproveOneAsync(admin, player, 50.31, taskType: "condition_report");
        Assert.True((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());
    }

    [Fact]
    public async Task Points_of_the_player_count_for_a_points_badge()
    {
        var admin = await api.AdminAsync();
        var key = Key();
        await CreateBadge(admin, key, new { type = "points", count = 30 });
        var (player, _, _) = await api.RegisterAsync("scorer");
        await ApproveOneAsync(admin, player, 50.4, reward: 25);
        var progress = (await BadgeOf(player, key)).GetProperty("progress");
        Assert.Equal((25, 30, 83), (progress.GetProperty("current").GetInt32(), progress.GetProperty("required").GetInt32(), progress.GetProperty("percent").GetInt32()));
        await ApproveOneAsync(admin, player, 50.41, reward: 25);
        Assert.True((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());
    }

    // ---- bonus points ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_badge_can_pay_a_bonus_once_and_the_bonus_can_lift_the_player_over_the_next_badge()
    {
        var admin = await api.AdminAsync();
        var first = Key();
        var second = Key();
        var firstId = (await Body(await CreateBadge(admin, first, new { type = "approved_submissions", count = 1 }, reward: 50))).GetProperty("id").GetGuid();
        var secondId = (await Body(await CreateBadge(admin, second, new { type = "points", count = 60 }, reward: 5))).GetProperty("id").GetGuid();   // 10 for the quest + 50 bonus = 60
        var (player, _, _) = await api.RegisterAsync("bonus");
        try
        {
            await ApproveOneAsync(admin, player, 50.5, reward: 10);

            Assert.True((await BadgeOf(player, first)).GetProperty("earned").GetBoolean());
            Assert.True((await BadgeOf(player, second)).GetProperty("earned").GetBoolean());   // reached only through the first bonus
            var me = await player.GetFromJsonAsync<JsonElement>("/me");
            Assert.Equal(10 + 50 + 5, me.GetProperty("totalPoints").GetInt32());

            var ledger = (await player.GetFromJsonAsync<JsonElement>("/me/points")).EnumerateArray().ToList();
            Assert.Equal(2, ledger.Count(l => l.GetProperty("reason").GetString() == "badge_reward"));
            Assert.Contains(ledger, l => l.GetProperty("amount").GetInt32() == 50 && l.GetProperty("reason").GetString() == "badge_reward");

            // the awards were announced as events for whoever wants to notify the player
            var awarded = await api.WithDb(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == nameof(BadgeAwarded)).Select(m => m.Payload).ToListAsync());
            Assert.Contains(awarded, p => p.Contains(first));
            Assert.Contains(awarded, p => p.Contains(second));
        }
        finally
        {
            // badges with a bonus would pay every later player of the other tests, too
            await admin.PutAsJsonAsync($"/admin/badges/{firstId}", new { isActive = false });
            await admin.PutAsJsonAsync($"/admin/badges/{secondId}", new { isActive = false });
        }
    }

    // ---- what admins can do ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_new_badge_goes_to_the_players_who_have_already_reached_it()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("early");
        await ApproveOneAsync(admin, player, 50.6);

        var key = Key();
        var created = await CreateBadge(admin, key, new { type = "approved_submissions", count = 1 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());
        Assert.True((await Body(created)).GetProperty("holders").GetInt32() >= 1);
    }

    [Fact]
    public async Task Changing_the_criteria_or_switching_a_badge_on_awards_to_those_who_qualify_and_switching_it_off_hides_it_but_keeps_what_was_earned()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("changer");
        await ApproveOneAsync(admin, player, 50.7);

        var key = Key();
        var created = await Body(await CreateBadge(admin, key, new { type = "approved_submissions", count = 5 }));
        var id = created.GetProperty("id").GetGuid();
        Assert.False((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());

        var lowered = await admin.PutAsJsonAsync($"/admin/badges/{id}", new { criteria = new { type = "approved_submissions", count = 1 } });
        Assert.Equal(HttpStatusCode.OK, lowered.StatusCode);
        Assert.True((await BadgeOf(player, key)).GetProperty("earned").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/admin/badges/{id}", new { isActive = false })).StatusCode);
        Assert.DoesNotContain((await player.GetFromJsonAsync<JsonElement>("/badges")).EnumerateArray(), b => b.GetProperty("key").GetString() == key);
        Assert.DoesNotContain((await player.GetFromJsonAsync<JsonElement>("/me/badges")).EnumerateArray(), b => b.GetProperty("key").GetString() == key);
        var listed = (await admin.GetFromJsonAsync<JsonElement>("/admin/badges")).EnumerateArray().Single(b => b.GetProperty("key").GetString() == key);
        Assert.False(listed.GetProperty("isActive").GetBoolean());
        Assert.True(listed.GetProperty("holders").GetInt32() >= 1);   // what was earned stays

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/admin/badges/{id}", new { isActive = true, name = "Neuer Name", rewardPoints = 0 })).StatusCode);
        Assert.Equal("Neuer Name", (await BadgeOf(player, key)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Admins_only_and_the_request_is_checked()
    {
        var admin = await api.AdminAsync();
        var (player, _, _) = await api.RegisterAsync("nosy");
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateBadge(player, Key(), new { type = "cards", count = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/badges")).StatusCode);

        async Task<HttpStatusCode> Try(object body) => (await admin.PostAsJsonAsync("/admin/badges", body)).StatusCode;
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = Key(), criteria = new { type = "cards", count = 1 } }));                          // no name
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = Key(), name = "x" }));                                                             // no criteria
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = Key(), name = "x", criteria = new { type = "telepathy", count = 1 } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = Key(), name = "x", criteria = new { type = "cards", count = 0 } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = Key(), name = "x", criteria = new { type = "rarity_cards", count = 1 } }));           // rarity missing
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = Key(), name = "x", criteria = new { type = "task_type", count = 1, taskType = "dance" } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = Key(), name = "x", criteria = new { type = "cards", count = 1 }, rewardPoints = -5 }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { key = "Bad Key!", name = "x", criteria = new { type = "cards", count = 1 } }));

        var key = Key();
        Assert.Equal(HttpStatusCode.Created, (await CreateBadge(admin, key, new { type = "cards", count = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreateBadge(admin, key, new { type = "cards", count = 1 })).StatusCode);   // the key is unique
        var id = (await admin.GetFromJsonAsync<JsonElement>("/admin/badges")).EnumerateArray().Single(b => b.GetProperty("key").GetString() == key).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/admin/badges/{id}", new { key = "another" })).StatusCode);          // the key never changes
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/admin/badges/{id}", new { criteria = new { type = "cards", count = -1 } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/admin/badges/{Guid.NewGuid()}", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task The_key_defaults_to_a_slug_of_the_name()
    {
        var admin = await api.AdminAsync();
        var name = "Baumfreund " + Guid.NewGuid().ToString("N")[..6];
        var res = await admin.PostAsJsonAsync("/admin/badges", new { name, criteria = new { type = "cards", count = 3 } });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.StartsWith("baumfreund-", (await Body(res)).GetProperty("key").GetString());
    }
}
