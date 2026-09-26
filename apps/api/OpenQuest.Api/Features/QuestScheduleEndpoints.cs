using System.Security.Claims;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Scheduling;
using OpenQuest.Api.Services;

namespace OpenQuest.Api.Features;

/// <summary>Weekly quest schedules: quests that come back every week, for example for trees nobody has checked for a long time.</summary>
public static class QuestScheduleEndpoints
{
    public static void MapQuestSchedules(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin/quest-schedules").RequireAuthorization("Admin").WithTags("Admin: recurring quests");

        g.MapGet("", async (IQuestScheduleAdmin schedules, CancellationToken ct) => Results.Ok(await schedules.ListAsync(ct)))
            .WithName("ListQuestSchedules").WithSummary("All schedules with the next planned run and the last run.");

        g.MapGet("/{id:guid}", async (Guid id, IQuestScheduleAdmin schedules, CancellationToken ct) =>
            (await schedules.GetAsync(id, ct)).ToHttp(Results.Ok)).WithName("GetQuestSchedule");

        g.MapPost("", async (QuestScheduleRequest req, ClaimsPrincipal user, IQuestScheduleAdmin schedules, CancellationToken ct) =>
            (await schedules.CreateAsync(user.GetUserId(), req, ct)).ToHttp(s => Results.Created($"/admin/quest-schedules/{s.Id}", s)))
            .WithName("CreateQuestSchedule")
            .WithSummary("Creates a weekly quest schedule. Every week at the given weekday and time (in the city's time zone) one quest is created per asset the target selects, open for durationHours. Example target for \"trees nobody checked for a year\": {\"notVerifiedForDays\":365,\"limit\":50}. The template is checked by creating its quests once and rolling back (400/422 with the reasons).");

        g.MapPut("/{id:guid}", async (Guid id, QuestScheduleRequest req, IQuestScheduleAdmin schedules, CancellationToken ct) =>
            (await schedules.UpdateAsync(id, req, ct)).ToHttp(Results.Ok))
            .WithName("UpdateQuestSchedule").WithSummary("Changes the given fields; set isEnabled to false to pause it.");

        g.MapDelete("/{id:guid}", async (Guid id, IQuestScheduleAdmin schedules, CancellationToken ct) =>
            (await schedules.DeleteAsync(id, ct)).ToHttp(_ => Results.NoContent()))
            .WithName("DeleteQuestSchedule").WithSummary("Deletes the schedule and its run history. Quests that were already created stay.");

        g.MapGet("/{id:guid}/runs", async (Guid id, int? limit, IQuestScheduleAdmin schedules, CancellationToken ct) =>
            (await schedules.RunsAsync(id, Math.Clamp(limit ?? 20, 1, 200), ct)).ToHttp(Results.Ok))
            .WithName("ListQuestScheduleRuns").WithSummary("The runs of a schedule, newest first (period, quests created, error).");

        g.MapPost("/{id:guid}/preview", async (Guid id, IQuestScheduleRunner runner, CancellationToken ct) =>
            (await runner.PreviewAsync(id, ct)).ToHttp(n => Results.Ok(new { wouldCreate = n })))
            .WithName("PreviewQuestSchedule").WithSummary("How many quests a run would create right now. Nothing is saved.");

        g.MapPost("/{id:guid}/run", async (Guid id, IQuestScheduleRunner runner, CancellationToken ct) =>
            (await runner.RunNowAsync(id, ct)).ToHttp(Results.Ok))
            .WithName("RunQuestScheduleNow").WithSummary("Creates the quests now, in addition to the weekly runs (also when the schedule is disabled).");
    }
}
