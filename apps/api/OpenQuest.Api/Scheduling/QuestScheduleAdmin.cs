using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Districts;
using OpenQuest.Api.Services;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Scheduling;

/// <summary>Admin side of the weekly quest schedules: list, create, change, delete.</summary>
public interface IQuestScheduleAdmin
{
    Task<IReadOnlyList<QuestScheduleDto>> ListAsync(CancellationToken ct);
    Task<ServiceResult<QuestScheduleDto>> GetAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<QuestScheduleDto>> CreateAsync(Guid adminId, QuestScheduleRequest req, CancellationToken ct);
    Task<ServiceResult<QuestScheduleDto>> UpdateAsync(Guid id, QuestScheduleRequest req, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct);
    Task<ServiceResult<IReadOnlyList<QuestScheduleRunDto>>> RunsAsync(Guid id, int limit, CancellationToken ct);
}

public sealed class QuestScheduleAdmin(AppDbContext db, IQuestScheduleRunner runner, TimeProvider clock) : IQuestScheduleAdmin
{
    public async Task<IReadOnlyList<QuestScheduleDto>> ListAsync(CancellationToken ct)
    {
        var schedules = await db.QuestSchedules.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var dtos = new List<QuestScheduleDto>();
        foreach (var s in schedules) dtos.Add(await ToDtoAsync(s, ct));
        return dtos;
    }

    public async Task<ServiceResult<QuestScheduleDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await db.QuestSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? ServiceResult<QuestScheduleDto>.Fail(404, "schedule_not_found") : ServiceResult<QuestScheduleDto>.Success(await ToDtoAsync(s, ct));
    }

    public async Task<ServiceResult<QuestScheduleDto>> CreateAsync(Guid adminId, QuestScheduleRequest req, CancellationToken ct)
    {
        var errors = Validate(req, creating: true);
        if (errors.Count > 0) return Invalid(errors);
        var now = clock.GetUtcNow();
        var s = new QuestSchedule { CreatedBy = adminId, CreatedAt = now, UpdatedAt = now };
        Apply(s, req);
        return await SaveAsync(s, isNew: true, ct);
    }

    public async Task<ServiceResult<QuestScheduleDto>> UpdateAsync(Guid id, QuestScheduleRequest req, CancellationToken ct)
    {
        var s = await db.QuestSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return ServiceResult<QuestScheduleDto>.Fail(404, "schedule_not_found");
        var errors = Validate(req, creating: false);
        if (errors.Count > 0) return Invalid(errors);
        Apply(s, req);
        s.UpdatedAt = clock.GetUtcNow();
        return await SaveAsync(s, isNew: false, ct);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var s = await db.QuestSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return ServiceResult<bool>.Fail(404, "schedule_not_found");
        db.QuestSchedules.Remove(s);   // the run history goes with it; quests already created stay
        await db.SaveChangesAsync(ct);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<IReadOnlyList<QuestScheduleRunDto>>> RunsAsync(Guid id, int limit, CancellationToken ct)
    {
        if (!await db.QuestSchedules.AnyAsync(x => x.Id == id, ct)) return ServiceResult<IReadOnlyList<QuestScheduleRunDto>>.Fail(404, "schedule_not_found");
        var runs = await db.QuestScheduleRuns.AsNoTracking().Where(r => r.ScheduleId == id).OrderByDescending(r => r.RanAt).Take(limit).ToListAsync(ct);
        return ServiceResult<IReadOnlyList<QuestScheduleRunDto>>.Success(runs.Select(QuestScheduleMapping.ToDto).ToList());
    }

    private async Task<ServiceResult<QuestScheduleDto>> SaveAsync(QuestSchedule s, bool isNew, CancellationToken ct)
    {
        var city = await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == s.CityId, ct);
        if (city is null) return Invalid(new() { ["cityId"] = ["Unknown city."] });
        if (await db.QuestSchedules.AnyAsync(x => x.Name == s.Name && x.Id != s.Id, ct))
            return ServiceResult<QuestScheduleDto>.Fail(409, "name_taken", "Another schedule already has this name.");
        if (await runner.ValidateAsync(s, ct) is { } problem)
            return ServiceResult<QuestScheduleDto>.Fail(problem.Status, problem.Code, problem.Message, problem.Details);

        // the check clears the change tracker (it created quests and rolled them back), so the schedule is attached again here
        if (isNew) db.QuestSchedules.Add(s); else db.QuestSchedules.Update(s);
        await db.SaveChangesAsync(ct);
        return ServiceResult<QuestScheduleDto>.Success(await ToDtoAsync(s, ct));
    }

    private async Task<QuestScheduleDto> ToDtoAsync(QuestSchedule s, CancellationToken ct)
    {
        var timezone = await db.Cities.AsNoTracking().Where(c => c.Id == s.CityId).Select(c => c.Timezone).FirstOrDefaultAsync(ct) ?? "UTC";
        var last = await db.QuestScheduleRuns.AsNoTracking().Where(r => r.ScheduleId == s.Id).OrderByDescending(r => r.RanAt).FirstOrDefaultAsync(ct);
        return QuestScheduleMapping.ToDto(s, Leaderboards.ResolveZone(timezone), clock.GetUtcNow(), last);
    }

    private static void Apply(QuestSchedule s, QuestScheduleRequest req)
    {
        if (req.Name is not null) s.Name = req.Name.Trim();
        if (req.CityId is { } cityId) s.CityId = cityId;
        if (req.Weekday is not null) { QuestScheduleMapping.TryParseWeekday(req.Weekday, out var day); s.Weekday = day; }
        if (req.Time is not null) { QuestScheduleMapping.TryParseTime(req.Time, out var time); s.TimeOfDay = time; }
        if (req.DurationHours is { } hours) s.DurationHours = hours;
        if (req.TaskType is not null) { TaskTypes.TryParse(req.TaskType, out var type); s.TaskType = type.Key(); }
        if (req.Title is not null) s.Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
        if (req.Description is not null) s.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.TaskConfig is not null) s.TaskConfig = req.TaskConfig.ToJsonString();
        if (req.Target is not null) s.Target = QuestScheduleMapping.WriteTarget(req.Target);
        if (req.MaxCompletions is { } max) s.MaxCompletions = max;
        if (req.RewardPoints is { } points) s.RewardPoints = points;
        if (req.GeofenceRadiusM is { } radius) s.GeofenceRadiusM = radius;
        if (req.ClaimTtlMinutes is { } ttl) s.ClaimTtlMinutes = ttl;
        if (req.IsEnabled is { } enabled) s.IsEnabled = enabled;
    }

    private static Dictionary<string, string[]> Validate(QuestScheduleRequest req, bool creating)
    {
        var errors = new Dictionary<string, string[]>();
        if (creating)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["A name is required."];
            if (req.CityId is null) errors["cityId"] = ["A city is required: its time zone decides when the week and the day start."];
            if (req.Weekday is null) errors["weekday"] = ["A weekday is required (monday to sunday)."];
            if (req.Time is null) errors["time"] = ["A time of day is required, HH:mm."];
            if (req.TaskType is null) errors["taskType"] = ["A task type is required."];
            if (req.Target is null) errors["target"] = ["A target is required."];
        }
        if (req.Name is not null && (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 128)) errors["name"] = ["1 to 128 characters."];
        if (req.Weekday is not null && !QuestScheduleMapping.TryParseWeekday(req.Weekday, out _)) errors["weekday"] = ["monday, tuesday, ... or sunday."];
        if (req.Time is not null && !QuestScheduleMapping.TryParseTime(req.Time, out _)) errors["time"] = ["Time of day as HH:mm, e.g. 08:00."];
        if (req.DurationHours is < 1 or > 168) errors["durationHours"] = ["Between 1 and 168 (one week)."];
        if (req.TaskType is not null && !TaskTypes.TryParse(req.TaskType, out _)) errors["taskType"] = ["Unknown task type."];
        if (req.MaxCompletions is < 1 or > 1000) errors["maxCompletions"] = ["Must be between 1 and 1000."];
        if (req.RewardPoints is < 0 or > 100_000) errors["rewardPoints"] = ["Must be between 0 and 100000."];
        return errors;
    }

    private static ServiceResult<QuestScheduleDto> Invalid(Dictionary<string, string[]> errors)
        => ServiceResult<QuestScheduleDto>.Fail(400, "validation_failed", "The request is not valid.", errors);
}
