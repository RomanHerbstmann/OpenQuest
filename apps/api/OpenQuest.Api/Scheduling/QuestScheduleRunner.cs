using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Districts;
using OpenQuest.Api.Services;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Scheduling;

/// <summary>Creates the quests of the weekly schedules.</summary>
public interface IQuestScheduleRunner
{
    /// <summary>Runs every schedule that is due and has not run in its current period yet. Returns the number of runs started.</summary>
    Task<int> RunDueAsync(CancellationToken ct);
    /// <summary>Runs one schedule now, on top of its weekly runs (also when it is disabled). The quests stay open for the schedule's duration.</summary>
    Task<ServiceResult<QuestScheduleRunDto>> RunNowAsync(Guid scheduleId, CancellationToken ct);
    /// <summary>How many quests a run would create right now. Nothing is saved.</summary>
    Task<ServiceResult<int>> PreviewAsync(Guid scheduleId, CancellationToken ct);
    /// <summary>Checks that a template is valid (task type, config, target) by creating its quests and rolling back. Null if it is valid.</summary>
    Task<ApiError?> ValidateAsync(QuestSchedule schedule, CancellationToken ct);
}

public sealed class QuestScheduleRunner(AppDbContext db, IQuestCampaignService campaigns, TimeProvider clock, ILogger<QuestScheduleRunner> log)
    : IQuestScheduleRunner
{
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var schedules = await (from s in db.QuestSchedules.AsNoTracking()
                               join c in db.Cities.AsNoTracking() on s.CityId equals c.Id
                               where s.IsEnabled && c.IsActive
                               select new { Schedule = s, c.Timezone }).ToListAsync(ct);
        var runs = 0;
        foreach (var item in schedules)
        {
            var s = item.Schedule;
            var zone = Leaderboards.ResolveZone(item.Timezone);
            if (WeeklySchedule.DueOccurrence(now, zone, s.Weekday, s.TimeOfDay) is not { } occurrence) continue;
            var endsAt = occurrence.At.AddHours(s.DurationHours);
            if (endsAt <= now) continue; // the window of this period is over (long downtime): wait for the next one
            if (await db.QuestScheduleRuns.AnyAsync(r => r.ScheduleId == s.Id && r.PeriodKey == occurrence.PeriodKey, ct)) continue;
            try
            {
                if (await RunAsync(s, occurrence.PeriodKey, occurrence.At, endsAt, ct) is not null) runs++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Running schedule '{Name}' for {Period} failed; it is tried again.", s.Name, occurrence.PeriodKey);
            }
            db.ChangeTracker.Clear();
        }
        return runs;
    }

    public async Task<ServiceResult<QuestScheduleRunDto>> RunNowAsync(Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.QuestSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is null) return ServiceResult<QuestScheduleRunDto>.Fail(404, "schedule_not_found");
        var now = clock.GetUtcNow();
        var run = await RunAsync(schedule, $"manual:{now:yyyyMMddTHHmmss}:{Guid.NewGuid():N}"[..40], now, now.AddHours(schedule.DurationHours), ct);
        return ServiceResult<QuestScheduleRunDto>.Success(QuestScheduleMapping.ToDto(run!));
    }

    public async Task<ServiceResult<int>> PreviewAsync(Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.QuestSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is null) return ServiceResult<int>.Fail(404, "schedule_not_found");
        var (created, error) = await TryCreateAndRollBackAsync(schedule, ct);
        return error is null ? ServiceResult<int>.Success(created) : ServiceResult<int>.Fail(error.Status, error.Code, error.Message, error.Details);
    }

    public async Task<ApiError?> ValidateAsync(QuestSchedule schedule, CancellationToken ct)
        => (await TryCreateAndRollBackAsync(schedule, ct)).Error;

    private async Task<(int Created, ApiError? Error)> TryCreateAndRollBackAsync(QuestSchedule schedule, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await campaigns.CreateAsync(schedule.CreatedBy, QuestScheduleMapping.ToRequest(schedule, now, now.AddHours(schedule.DurationHours)), ct);
            return (result.Value?.Created ?? 0, result.Error);
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Creates the quests for one period. The run's row is inserted first, in the same transaction, and its primary key lets only one
    /// caller through per schedule and period: a restart, a second instance or a repeated tick finds the row and does nothing (returns null).
    /// A run that cannot create quests (the template became invalid) is recorded with its error and not retried in this period.
    /// </summary>
    private async Task<QuestScheduleRun?> RunAsync(QuestSchedule schedule, string periodKey, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO quest_schedule_run (schedule_id, period_key, ran_at, quests_created)
            VALUES ({schedule.Id}, {periodKey}, {now}, 0) ON CONFLICT DO NOTHING
            """, ct);
        if (claimed == 0) { await tx.RollbackAsync(ct); return null; }

        var result = await campaigns.CreateAsync(schedule.CreatedBy, QuestScheduleMapping.ToRequest(schedule, startsAt, endsAt), ct);
        var run = new QuestScheduleRun { ScheduleId = schedule.Id, PeriodKey = periodKey, RanAt = now };
        if (result.Ok)
        {
            run.CampaignId = result.Value!.CampaignId;
            run.QuestsCreated = result.Value.Created;
            log.LogInformation("Schedule '{Name}' ({Period}) created {Count} quests.", schedule.Name, periodKey, run.QuestsCreated);
        }
        else
        {
            run.Error = $"{result.Error!.Code}: {result.Error.Message} {JsonSerializer.Serialize(result.Error.Details)}".Trim();
            log.LogWarning("Schedule '{Name}' ({Period}) could not create quests: {Error}", schedule.Name, periodKey, run.Error);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE quest_schedule_run SET campaign_id = {run.CampaignId}, quests_created = {run.QuestsCreated}, error = {run.Error}
            WHERE schedule_id = {schedule.Id} AND period_key = {periodKey}
            """, ct);
        await tx.CommitAsync(ct);
        return run;
    }
}
