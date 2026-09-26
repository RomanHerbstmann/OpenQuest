using System.Globalization;

namespace OpenQuest.Core.Rules;

/// <summary>One occurrence of a weekly schedule. <paramref name="PeriodKey"/> is the ISO week it belongs to and identifies it: "2026-W39".</summary>
public readonly record struct ScheduleOccurrence(string PeriodKey, DateTimeOffset At);   // At is in UTC (the database only takes offset 0)

/// <summary>
/// A schedule that fires once per calendar week on a weekday and a time of day in a city's time zone (for example
/// "Sunday 08:00"). Pure functions: the caller keeps track of which periods already ran, so a restart never fires twice.
/// </summary>
public static class WeeklySchedule
{
    /// <summary>The ISO week (Monday to Sunday) that <paramref name="now"/> lies in, as seen in <paramref name="zone"/>: "2026-W39".</summary>
    public static string PeriodKey(DateTimeOffset now, TimeZoneInfo zone)
        => KeyOf(MondayOf(TimeZoneInfo.ConvertTime(now, zone).Date));

    /// <summary>
    /// This week's occurrence if its moment has been reached, otherwise null. Once it is reached it stays "due" for the rest of the
    /// week, so a service that was down at the exact time still catches up. A time that does not exist (spring forward) moves to the next hour.
    /// </summary>
    public static ScheduleOccurrence? DueOccurrence(DateTimeOffset now, TimeZoneInfo zone, DayOfWeek day, TimeOnly time)
    {
        var occurrence = OccurrenceOfWeek(MondayOf(TimeZoneInfo.ConvertTime(now, zone).Date), zone, day, time);
        return now >= occurrence.At ? occurrence : null;
    }

    /// <summary>The first occurrence after <paramref name="now"/>: this week's if it is still ahead, otherwise next week's.</summary>
    public static ScheduleOccurrence NextOccurrence(DateTimeOffset now, TimeZoneInfo zone, DayOfWeek day, TimeOnly time)
    {
        var monday = MondayOf(TimeZoneInfo.ConvertTime(now, zone).Date);
        var occurrence = OccurrenceOfWeek(monday, zone, day, time);
        return occurrence.At > now ? occurrence : OccurrenceOfWeek(monday.AddDays(7), zone, day, time);
    }

    private static ScheduleOccurrence OccurrenceOfWeek(DateTime monday, TimeZoneInfo zone, DayOfWeek day, TimeOnly time)
    {
        var local = monday.AddDays(((int)day + 6) % 7).Add(time.ToTimeSpan());
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        // for an ambiguous time (fall back) the earlier of the two moments counts
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new ScheduleOccurrence(KeyOf(monday), new DateTimeOffset(local, offset).ToUniversalTime());
    }

    private static DateTime MondayOf(DateTime localDate) => localDate.AddDays(-(((int)localDate.DayOfWeek + 6) % 7));

    private static string KeyOf(DateTime monday)
        => $"{ISOWeek.GetYear(monday):0000}-W{ISOWeek.GetWeekOfYear(monday):00}";
}
