using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenQuest.Api.Data;
using OpenQuest.Api.Gamification;
using OpenQuest.Api.Scheduling;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Tests;

/// <summary>
/// Recurring quests for trees nobody has checked for a long time: what players verified when (asset_activity), the selection of
/// assets by district, city and staleness, the weekly schedules and their runner, and the announcement of finished syncs.
/// Every test works in its own patch of the map, so districts of different tests never touch each other.
/// </summary>
[Collection(ApiCollection.Name)]
public class StaleQuestTests(ApiFactory api)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private const double Lon = 7.0;

    private sealed record Area(HttpClient Admin, Guid CityId, Guid DistrictId, double Lat);

    /// <summary>A new city with one district (a 0.02° square from <paramref name="lat"/>/<see cref="Lon"/> up and to the east).</summary>
    private async Task<Area> NewAreaAsync(double lat)
    {
        var admin = await api.AdminAsync();
        var city = await admin.PostAsJsonAsync("/admin/cities", new { name = "Stale " + Guid.NewGuid().ToString("N")[..8], timezone = "Europe/Berlin" });
        Assert.Equal(HttpStatusCode.Created, city.StatusCode);
        var cityId = (await city.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var ring = new JsonArray(new JsonArray(Lon, lat), new JsonArray(Lon + 0.02, lat), new JsonArray(Lon + 0.02, lat + 0.02), new JsonArray(Lon, lat + 0.02), new JsonArray(Lon, lat));
        var geometry = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(ring) };
        var district = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Mitte", geometry });
        Assert.True(district.IsSuccessStatusCode, await district.Content.ReadAsStringAsync());
        return new Area(admin, cityId, (await district.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), lat);
    }

    /// <summary>A tree inside the area's district; <paramref name="n"/> spreads several trees a little.</summary>
    private Task<Guid> TreeAsync(Area a, int n = 0) => api.AddTreeAsync(a.Lat + 0.001 + n * 0.0005, Lon + 0.001);

    /// <summary>Lets the player complete a quest for the tree and a moderator approve it; waits until the event reached its handlers.</summary>
    private async Task<Guid> VerifyAsync(HttpClient admin, HttpClient player, Guid assetId, string attribute = "genus")
    {
        var (lat, lon) = await api.WithDb(async db =>
        {
            var p = (await db.Assets.AsNoTracking().FirstAsync(x => x.Id == assetId)).Geom;
            return (p.Y, p.X);
        });
        var questId = await api.CreateQuestAsync(admin, assetId, taskConfig: new { attribute });
        var claim = await (await player.PostAsync($"/quests/{questId}/claim", null)).Content.ReadFromJsonAsync<JsonElement>();
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(new { value = "Tilia" })), "payload" },
        };
        var submitted = await player.PostAsync($"/claims/{claim.GetProperty("id").GetGuid()}/submit", form);
        Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
        var submissionId = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        await WaitForAsync(() => api.WithDb(db => db.OutboxMessages.AnyAsync(m => m.Type == nameof(SubmissionApproved) && m.Status == OutboxStatus.Processed
            && EF.Functions.JsonContains(m.Payload, "{\"submissionId\":\"" + submissionId + "\"}"))), "The approval was not delivered.");
        return submissionId;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException(message + " (within 20 s)");
    }

    private async Task<Guid[]> QuestAssetsOfAsync(HttpClient admin, Guid campaignId)
        => await api.WithDb(async db => (await db.Quests.AsNoTracking().Where(q => q.CampaignId == campaignId).Select(q => q.AssetId!.Value).ToListAsync()).ToArray());

    /// <summary>A schedule that is due today: today's weekday, midnight in Berlin, open for a week.</summary>
    private object DueScheduleBody(Guid cityId, string name, object target, int rewardPoints = 25, string? weekday = null, string time = "00:00", int durationHours = 168)
        => new
        {
            name, cityId, weekday = weekday ?? TimeZoneInfo.ConvertTime(api.Clock.GetUtcNow(), Berlin).DayOfWeek.ToString().ToLowerInvariant(), time, durationHours,
            taskType = "photo", title = "Sunday check", maxCompletions = 1, rewardPoints, target,
        };

    // ---- what players verified when -----------------------------------------------------------------------------------

    [Fact]
    public async Task An_approved_submission_records_when_the_asset_was_last_verified_and_how_often()
    {
        var area = await NewAreaAsync(30.0);
        var (player, _, _) = await api.RegisterAsync("verifier");
        var tree = await TreeAsync(area);
        Assert.False(await api.WithDb(db => db.AssetActivities.AnyAsync(x => x.AssetId == tree)));   // never verified: no row

        var before = api.Clock.GetUtcNow();
        await VerifyAsync(area.Admin, player, tree);
        var first = await api.WithDb(db => db.AssetActivities.AsNoTracking().SingleAsync(x => x.AssetId == tree));
        Assert.Equal(1, first.VerificationCount);
        Assert.True(first.LastVerifiedAt >= before.AddSeconds(-1));

        api.Clock.Advance(TimeSpan.FromMinutes(45));
        await VerifyAsync(area.Admin, player, tree, attribute: "species");   // another quest for the same tree
        var second = await api.WithDb(db => db.AssetActivities.AsNoTracking().SingleAsync(x => x.AssetId == tree));
        Assert.Equal(2, second.VerificationCount);
        Assert.True(second.LastVerifiedAt > first.LastVerifiedAt);
    }

    [Fact]
    public async Task Delivering_the_same_approval_again_changes_nothing()
    {
        var area = await NewAreaAsync(30.1);
        var (player, _, _) = await api.RegisterAsync("twice");
        var tree = await TreeAsync(area);
        var submissionId = await VerifyAsync(area.Admin, player, tree);
        var questId = await api.WithDb(db => db.Submissions.Where(s => s.Id == submissionId).Select(s => s.Claim.QuestId).SingleAsync());

        using var scope = api.Services.CreateScope();
        var handler = scope.ServiceProvider.GetServices<IEventHandler<SubmissionApproved>>().OfType<AssetActivityHandler>().Single();
        var again = new SubmissionApproved(submissionId, Guid.NewGuid(), questId, 10);
        await handler.HandleAsync([again], CancellationToken.None);
        await handler.HandleAsync([again, again], CancellationToken.None);

        Assert.Equal(1, (await api.WithDb(db => db.AssetActivities.AsNoTracking().SingleAsync(x => x.AssetId == tree))).VerificationCount);
    }

    // ---- selecting assets ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Quests_can_target_the_assets_nobody_verified_for_a_long_time_the_longest_unchecked_first()
    {
        var area = await NewAreaAsync(30.2);
        var now = api.Clock.GetUtcNow();
        var recent = await TreeAsync(area, 0);
        var old = await TreeAsync(area, 1);
        var never = await TreeAsync(area, 2);
        var outside = await api.AddTreeAsync(area.Lat + 0.5, Lon);   // never verified as well, but not in the district
        await api.WithDb(async db =>
        {
            db.AssetActivities.Add(new AssetActivity { AssetId = recent, LastVerifiedAt = now.AddDays(-10), VerificationCount = 1 });
            db.AssetActivities.Add(new AssetActivity { AssetId = old, LastVerifiedAt = now.AddDays(-400), VerificationCount = 3 });
            await db.SaveChangesAsync();
            return 0;
        });

        async Task<JsonElement> Create(object target) => await (await area.Admin.PostAsJsonAsync("/admin/quests", new { taskType = "photo", maxCompletions = 1, rewardPoints = 5, target })).Content.ReadFromJsonAsync<JsonElement>();

        // the most stale one only: never verified beats verified long ago
        var one = await Create(new { districtId = area.DistrictId, notVerifiedForDays = 365, limit = 1 });
        Assert.Equal(never, (await QuestAssetsOfAsync(area.Admin, one.GetProperty("campaignId").GetGuid())).Single());

        // then the rest: the tree checked 400 days ago, but not the one checked 10 days ago and not the one outside
        var rest = await Create(new { districtId = area.DistrictId, notVerifiedForDays = 365 });
        Assert.Equal([old], await QuestAssetsOfAsync(area.Admin, rest.GetProperty("campaignId").GetGuid()));

        // a shorter period reaches the tree checked 10 days ago too (the others already have a quest)
        var recentToo = await Create(new { districtId = area.DistrictId, notVerifiedForDays = 5 });
        Assert.Equal([recent], await QuestAssetsOfAsync(area.Admin, recentToo.GetProperty("campaignId").GetGuid()));

        // by city: any district of the city, so still not the tree outside
        var byCity = await area.Admin.PostAsJsonAsync("/admin/quests", new { taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { cityId = area.CityId, notVerifiedForDays = 1 } });
        Assert.Equal(0, (await byCity.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());   // all three have quests already
        Assert.DoesNotContain(outside, await api.WithDb(db => db.Quests.Where(q => q.AssetId == outside).Select(q => q.AssetId).ToListAsync()));
    }

    [Fact]
    public async Task A_target_needs_a_selector_and_unknown_districts_and_cities_are_refused()
    {
        var admin = await api.AdminAsync();
        var none = await admin.PostAsJsonAsync("/admin/quests", new { taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { limit = 5 } });
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        var district = await admin.PostAsJsonAsync("/admin/quests", new { taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { districtId = Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, district.StatusCode);
        var city = await admin.PostAsJsonAsync("/admin/quests", new { taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { cityId = Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, city.StatusCode);
        var days = await admin.PostAsJsonAsync("/admin/quests", new { taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { notVerifiedForDays = 0 } });
        Assert.Equal(HttpStatusCode.BadRequest, days.StatusCode);
    }

    [Fact]
    public async Task A_quest_whose_end_has_passed_no_longer_blocks_a_new_one_for_the_same_asset()
    {
        var area = await NewAreaAsync(30.3);
        var tree = await TreeAsync(area);
        var first = await area.Admin.PostAsJsonAsync("/admin/quests", new
        {
            taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { assetIds = new[] { tree } }, endsAt = api.Clock.GetUtcNow().AddHours(1),
        });
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());

        object body = new { taskType = "photo", maxCompletions = 1, rewardPoints = 5, target = new { assetIds = new[] { tree } } };
        Assert.Equal(0, (await (await area.Admin.PostAsJsonAsync("/admin/quests", body)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());   // still open

        api.Clock.Advance(TimeSpan.FromHours(2));   // the first quest ended
        Assert.Equal(1, (await (await area.Admin.PostAsJsonAsync("/admin/quests", body)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
    }

    /// <summary>Lets the runner check all schedules (those of other tests too) and returns how often the given one has run so far.</summary>
    private async Task<int> TickAsync(Guid scheduleId)
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IQuestScheduleRunner>().RunDueAsync(CancellationToken.None);
        return await api.WithDb(db => db.QuestScheduleRuns.CountAsync(r => r.ScheduleId == scheduleId));
    }

    // ---- schedules ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Schedules_are_managed_by_admins_and_validated_when_they_are_saved()
    {
        var area = await NewAreaAsync(30.4);
        var (player, _, _) = await api.RegisterAsync("nosy");
        var body = DueScheduleBody(area.CityId, "Weekly " + Guid.NewGuid().ToString("N")[..6], new { districtId = area.DistrictId, notVerifiedForDays = 365, limit = 10 }, weekday: "sunday", time: "08:00");

        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync("/admin/quest-schedules", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.GetAsync("/admin/quest-schedules")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/admin/quest-schedules")).StatusCode);

        var created = await area.Admin.PostAsJsonAsync("/admin/quest-schedules", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var schedule = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = schedule.GetProperty("id").GetGuid();
        Assert.Equal("sunday", schedule.GetProperty("weekday").GetString());
        Assert.Equal("08:00", schedule.GetProperty("time").GetString());
        Assert.True(schedule.GetProperty("isEnabled").GetBoolean());
        var next = schedule.GetProperty("nextRunAt").GetDateTimeOffset();
        Assert.True(next > api.Clock.GetUtcNow());
        var nextLocal = TimeZoneInfo.ConvertTime(next, Berlin);   // Sunday 08:00 in the city's time zone
        Assert.Equal((DayOfWeek.Sunday, 8, 0), (nextLocal.DayOfWeek, nextLocal.Hour, nextLocal.Minute));
        Assert.Equal(JsonValueKind.Null, schedule.GetProperty("lastRun").ValueKind);

        Assert.Equal(HttpStatusCode.Conflict, (await area.Admin.PostAsJsonAsync("/admin/quest-schedules", body)).StatusCode);   // name is unique

        // the pieces are checked: weekday, time, duration, task type, city, target
        async Task<HttpStatusCode> Try(object change) => (await area.Admin.PutAsJsonAsync($"/admin/quest-schedules/{id}", change)).StatusCode;
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { weekday = "someday" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { time = "25:00" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { durationHours = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { taskType = "dance" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { cityId = Guid.NewGuid() }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { target = new { notVerifiedForDays = 0 } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(new { taskType = "verify_attribute" }));                 // needs an attribute in taskConfig
        Assert.Equal(HttpStatusCode.NotFound, (await area.Admin.PutAsJsonAsync($"/admin/quest-schedules/{Guid.NewGuid()}", new { name = "x" })).StatusCode);

        // changes are saved (also after the validation, which rolls its trial quests back)
        var updated = await area.Admin.PutAsJsonAsync($"/admin/quest-schedules/{id}", new { weekday = "saturday", time = "09:30", rewardPoints = 40, isEnabled = false });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var read = await (await area.Admin.GetAsync($"/admin/quest-schedules/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("saturday", "09:30", 40, false), (read.GetProperty("weekday").GetString(), read.GetProperty("time").GetString(), read.GetProperty("rewardPoints").GetInt32(), read.GetProperty("isEnabled").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, read.GetProperty("nextRunAt").ValueKind);   // paused
        Assert.Contains((await area.Admin.GetFromJsonAsync<JsonElement>("/admin/quest-schedules")).EnumerateArray(), s => s.GetProperty("id").GetGuid() == id);

        // validating and previewing create nothing
        Assert.Equal(0, await api.WithDb(db => db.Quests.CountAsync(q => q.Title == "Sunday check")));

        Assert.Equal(HttpStatusCode.NoContent, (await area.Admin.DeleteAsync($"/admin/quest-schedules/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await area.Admin.GetAsync($"/admin/quest-schedules/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_due_schedule_creates_its_quests_once_per_week_with_the_bonus_and_the_window()
    {
        var area = await NewAreaAsync(30.5);
        var stale = await TreeAsync(area, 0);
        var fresh = await TreeAsync(area, 1);
        await api.WithDb(async db =>
        {
            db.AssetActivities.Add(new AssetActivity { AssetId = fresh, LastVerifiedAt = api.Clock.GetUtcNow().AddDays(-3), VerificationCount = 1 });
            await db.SaveChangesAsync();
            return 0;
        });
        var created = await area.Admin.PostAsJsonAsync("/admin/quest-schedules",
            DueScheduleBody(area.CityId, "Weekly " + Guid.NewGuid().ToString("N")[..6], new { notVerifiedForDays = 365 }, rewardPoints: 25));   // no district: the schedule's city
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(1, (await (await area.Admin.PostAsync($"/admin/quest-schedules/{id}/preview", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("wouldCreate").GetInt32());

        var now = api.Clock.GetUtcNow();
        Assert.Equal(1, await TickAsync(id));

        var run = (await area.Admin.GetFromJsonAsync<JsonElement>($"/admin/quest-schedules/{id}/runs")).EnumerateArray().Single();
        Assert.Equal(1, run.GetProperty("questsCreated").GetInt32());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("error").ValueKind);
        var campaignId = run.GetProperty("campaignId").GetGuid();
        Assert.Equal([stale], await QuestAssetsOfAsync(area.Admin, campaignId));   // only the tree that was not checked for a year

        var quest = await api.WithDb(db => db.Quests.AsNoTracking().SingleAsync(q => q.CampaignId == campaignId));
        Assert.Equal(25, quest.RewardPoints);
        Assert.Equal("Sunday check", quest.Title);
        Assert.Equal(QuestStatus.Active, quest.Status);
        // today's midnight in Berlin, open for 168 hours
        var midnight = TimeZoneInfo.ConvertTime(now, Berlin).Date;
        var expectedStart = new DateTimeOffset(midnight, Berlin.GetUtcOffset(midnight));
        Assert.Equal(expectedStart, quest.StartsAt);
        Assert.Equal(expectedStart.AddHours(168), quest.EndsAt);

        // the next ticks of the same week do nothing, also when two run at the same moment
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => TickAsync(id)));
        Assert.Equal(1, (await area.Admin.GetFromJsonAsync<JsonElement>($"/admin/quest-schedules/{id}/runs")).GetArrayLength());
        Assert.Equal(1, await api.WithDb(db => db.Quests.CountAsync(q => q.Title == "Sunday check" && q.AssetId == stale)));

        var listed = (await area.Admin.GetFromJsonAsync<JsonElement>("/admin/quest-schedules")).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);
        Assert.Equal(campaignId, listed.GetProperty("lastRun").GetProperty("campaignId").GetGuid());
        Assert.True(listed.GetProperty("nextRunAt").GetDateTimeOffset() > api.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Next_week_the_schedule_runs_again_and_last_weeks_unfinished_quests_do_not_block_it()
    {
        var area = await NewAreaAsync(30.6);
        var tree = await TreeAsync(area);
        var created = await area.Admin.PostAsJsonAsync("/admin/quest-schedules",
            DueScheduleBody(area.CityId, "Weekly " + Guid.NewGuid().ToString("N")[..6], new { notVerifiedForDays = 365 }, durationHours: 24));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(1, await TickAsync(id));
        Assert.Equal(1, await TickAsync(id));

        api.Clock.Advance(TimeSpan.FromDays(7));   // the same weekday of the next ISO week; the 24 h window of the last run is over
        Assert.Equal(2, await TickAsync(id));
        var runs = (await area.Admin.GetFromJsonAsync<JsonElement>($"/admin/quest-schedules/{id}/runs")).EnumerateArray().ToList();
        Assert.Equal(2, runs.Count);
        Assert.NotEqual(runs[0].GetProperty("periodKey").GetString(), runs[1].GetProperty("periodKey").GetString());
        Assert.All(runs, r => Assert.Equal(1, r.GetProperty("questsCreated").GetInt32()));
        Assert.Equal(2, await api.WithDb(db => db.Quests.CountAsync(q => q.AssetId == tree)));   // one per week for the same tree
    }

    [Fact]
    public async Task A_schedule_that_is_paused_or_not_yet_due_does_not_run()
    {
        var area = await NewAreaAsync(30.7);
        await TreeAsync(area);
        var name = "Weekly " + Guid.NewGuid().ToString("N")[..6];
        var paused = await area.Admin.PostAsJsonAsync("/admin/quest-schedules", DueScheduleBody(area.CityId, name + "a", new { notVerifiedForDays = 30 }));
        var pausedId = (await paused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await area.Admin.PutAsJsonAsync($"/admin/quest-schedules/{pausedId}", new { isEnabled = false });

        // today's weekday but a time of day that lies ahead in the same week is not reachable on a Sunday for "later today" (23:59 may already be past): use the next day
        // of the week only when it is still this ISO week, otherwise take a time late tonight
        var local = TimeZoneInfo.ConvertTime(api.Clock.GetUtcNow(), Berlin);
        var later = local.DayOfWeek == DayOfWeek.Sunday ? local.AddMinutes(1) : local.AddDays(1);
        var laterWeekday = later.DayOfWeek.ToString().ToLowerInvariant();
        var laterTime = local.DayOfWeek == DayOfWeek.Sunday ? later.ToString("HH:mm") : "23:59";
        if (local.DayOfWeek == DayOfWeek.Sunday && local.Hour == 23 && local.Minute == 59) return;   // nothing lies ahead this week
        var ahead = await area.Admin.PostAsJsonAsync("/admin/quest-schedules", DueScheduleBody(area.CityId, name + "b", new { notVerifiedForDays = 30 }, weekday: laterWeekday, time: laterTime));
        Assert.Equal(HttpStatusCode.Created, ahead.StatusCode);

        var aheadId = (await ahead.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(0, await TickAsync(pausedId));
        Assert.Equal(0, await TickAsync(aheadId));

        // on request it runs anyway, also the paused one
        var now = await area.Admin.PostAsync($"/admin/quest-schedules/{pausedId}/run", null);
        Assert.Equal(HttpStatusCode.OK, now.StatusCode);
        Assert.StartsWith("manual:", (await now.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("periodKey").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await area.Admin.PostAsync($"/admin/quest-schedules/{Guid.NewGuid()}/run", null)).StatusCode);
    }

    [Fact]
    public async Task A_run_that_cannot_create_quests_is_recorded_with_its_error_and_not_repeated()
    {
        var area = await NewAreaAsync(30.8);
        var created = await area.Admin.PostAsJsonAsync("/admin/quest-schedules", DueScheduleBody(area.CityId, "Weekly " + Guid.NewGuid().ToString("N")[..6], new { notVerifiedForDays = 30 }));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // the district the template points to is gone from the database behind the API's back
        await api.WithDb(async db =>
        {
            var schedule = await db.QuestSchedules.SingleAsync(s => s.Id == id);
            schedule.Target = JsonSerializer.Serialize(new { districtId = Guid.NewGuid() });
            await db.SaveChangesAsync();
            return 0;
        });

        Assert.Equal(1, await TickAsync(id));
        Assert.Equal(1, await TickAsync(id));

        var run = (await area.Admin.GetFromJsonAsync<JsonElement>($"/admin/quest-schedules/{id}/runs")).EnumerateArray().Single();
        Assert.Equal(0, run.GetProperty("questsCreated").GetInt32());
        Assert.Contains("validation_failed", run.GetProperty("error").GetString());
    }

    // ---- finished syncs ------------------------------------------------------------------------------------------------------

    private Task<Guid> AddRunAsync(string status, int created, int updated, int removed) => api.WithDb(async db =>
    {
        var source = await db.DataSources.FirstAsync();
        var run = new SyncRun
        {
            DataSourceId = source.Id, StartedAt = api.Clock.GetUtcNow(), FinishedAt = api.Clock.GetUtcNow(), Status = status,
            AssetsCreated = created, AssetsUpdated = updated, AssetsRemoved = removed,
        };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    });

    /// <summary>What the importer does after committing a run.</summary>
    private Task NotifyAsync(Guid runId) => api.WithDb(async db =>
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify('sync_finished', {runId.ToString()})");
        return 0;
    });

    private Task<List<OutboxMessage>> AnnouncementsAsync(Guid runId) => api.WithDb(db => db.OutboxMessages.AsNoTracking()
        .Where(m => m.Type == nameof(AssetSyncCompleted) && EF.Functions.JsonContains(m.Payload, "{\"runId\":\"" + runId + "\"}")).ToListAsync());

    [Fact]
    public async Task A_finished_sync_becomes_an_event_that_marks_the_genus_statistics_as_out_of_date()
    {
        var area = await NewAreaAsync(31.0);
        await api.WithDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE district SET genus_stats_at = now() WHERE id = {area.DistrictId}");
            return 0;
        });
        var runId = await AddRunAsync(RunStatus.Succeeded, 3, 1, 0);

        await NotifyAsync(runId);
        await WaitForAsync(async () => (await AnnouncementsAsync(runId)).Any(m => m.Status == OutboxStatus.Processed), "The sync was not announced.");

        var message = Assert.Single(await AnnouncementsAsync(runId));
        var payload = JsonDocument.Parse(message.Payload).RootElement;
        Assert.Equal(("de-muenster-trees", 3, 1, 0), (payload.GetProperty("dataSourceKey").GetString(), payload.GetProperty("assetsCreated").GetInt32(),
            payload.GetProperty("assetsUpdated").GetInt32(), payload.GetProperty("assetsRemoved").GetInt32()));
        Assert.Null(await api.WithDb(db => db.Districts.AsNoTracking().Where(d => d.Id == area.DistrictId).Select(d => d.GenusStatsAt).SingleAsync()));
    }

    [Fact]
    public async Task A_notification_is_announced_only_once_a_failed_run_never_and_a_run_without_changes_keeps_the_statistics()
    {
        var area = await NewAreaAsync(31.1);
        await api.WithDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE district SET genus_stats_at = now() WHERE id = {area.DistrictId}");
            return 0;
        });
        var unchanged = await AddRunAsync(RunStatus.Succeeded, 0, 0, 0);
        var failed = await AddRunAsync(RunStatus.Failed, 5, 0, 0);
        var marker = await AddRunAsync(RunStatus.Succeeded, 1, 0, 0);   // announced last: when it arrived, the earlier notifications were handled

        await NotifyAsync(unchanged);
        await NotifyAsync(unchanged);
        await NotifyAsync(failed);
        await NotifyAsync(marker);
        await WaitForAsync(async () => (await AnnouncementsAsync(marker)).Any(m => m.Status == OutboxStatus.Processed)
                                       && (await AnnouncementsAsync(unchanged)).All(m => m.Status == OutboxStatus.Processed) && (await AnnouncementsAsync(unchanged)).Count > 0,
            "The notifications were not handled.");

        Assert.Single(await AnnouncementsAsync(unchanged));   // twice notified, once announced
        Assert.Empty(await AnnouncementsAsync(failed));
        // the marker run did change assets, so by now the statistics are marked; the run without changes alone would not have done it
        Assert.Null(await api.WithDb(db => db.Districts.AsNoTracking().Where(d => d.Id == area.DistrictId).Select(d => d.GenusStatsAt).SingleAsync()));
    }

    [Fact]
    public async Task The_handler_leaves_the_statistics_alone_when_a_sync_changed_nothing()
    {
        var area = await NewAreaAsync(31.2);
        await api.WithDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE district SET genus_stats_at = now() WHERE id = {area.DistrictId}");
            return 0;
        });
        using var scope = api.Services.CreateScope();
        var handler = scope.ServiceProvider.GetServices<IEventHandler<AssetSyncCompleted>>().OfType<InvalidateGenusStatsHandler>().Single();
        await handler.HandleAsync([new AssetSyncCompleted(Guid.NewGuid(), Guid.NewGuid(), "x", 0, 0, 0)], CancellationToken.None);
        Assert.NotNull(await api.WithDb(db => db.Districts.AsNoTracking().Where(d => d.Id == area.DistrictId).Select(d => d.GenusStatsAt).SingleAsync()));
        await handler.HandleAsync([new AssetSyncCompleted(Guid.NewGuid(), Guid.NewGuid(), "x", 0, 2, 0)], CancellationToken.None);
        Assert.Null(await api.WithDb(db => db.Districts.AsNoTracking().Where(d => d.Id == area.DistrictId).Select(d => d.GenusStatsAt).SingleAsync()));
    }
}
