using System.Text.Json;
using System.Text.Json.Nodes;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Eventing;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Scheduling;

internal static class QuestScheduleMapping
{
    public static QuestTarget ReadTarget(QuestSchedule s)
        => JsonSerializer.Deserialize<QuestTarget>(s.Target, EventJson.Options) ?? new QuestTarget(null, null, null, null, null, null, null);

    public static string WriteTarget(QuestTarget t) => JsonSerializer.Serialize(t, EventJson.Options);

    public static string WeekdayName(DayOfWeek d) => d.ToString().ToLowerInvariant();

    public static bool TryParseWeekday(string? text, out DayOfWeek day)
    {
        foreach (var d in Enum.GetValues<DayOfWeek>())
            if (string.Equals(d.ToString(), text?.Trim(), StringComparison.OrdinalIgnoreCase)) { day = d; return true; }
        day = default;
        return false;
    }

    public static bool TryParseTime(string? text, out TimeOnly time)
        => TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm"], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);

    /// <summary>The quest campaign a run of the schedule creates. Without a district or city the target defaults to the schedule's city.</summary>
    public static CreateQuestsRequest ToRequest(QuestSchedule s, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var target = ReadTarget(s);
        if (target.DistrictId is null && target.CityId is null) target = target with { CityId = s.CityId };
        return new CreateQuestsRequest(
            s.TaskType, s.Title, s.Description, JsonNode.Parse(s.TaskConfig) as JsonObject, s.MaxCompletions, s.RewardPoints,
            s.GeofenceRadiusM, s.ClaimTtlMinutes, startsAt, endsAt, QuestStatus.Active, target);
    }

    public static QuestScheduleDto ToDto(QuestSchedule s, TimeZoneInfo zone, DateTimeOffset now, QuestScheduleRun? lastRun) => new(
        s.Id, s.Name, s.CityId, s.IsEnabled, WeekdayName(s.Weekday), s.TimeOfDay.ToString("HH:mm"), s.DurationHours, s.TaskType, s.Title,
        s.Description, JsonNode.Parse(s.TaskConfig), s.MaxCompletions, s.RewardPoints, s.GeofenceRadiusM, s.ClaimTtlMinutes, ReadTarget(s),
        s.IsEnabled ? WeeklySchedule.NextOccurrence(now, zone, s.Weekday, s.TimeOfDay).At : null,
        lastRun is null ? null : ToDto(lastRun));

    public static QuestScheduleRunDto ToDto(QuestScheduleRun r) => new(r.PeriodKey, r.RanAt, r.CampaignId, r.QuestsCreated, r.Error);
}
