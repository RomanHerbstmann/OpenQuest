using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class WeeklyScheduleTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly TimeOnly Eight = new(8, 0);

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_is_due_before_the_moment_is_reached()
    {
        // Saturday 2026-09-26; Sunday 08:00 local is still ahead
        Assert.Null(WeeklySchedule.DueOccurrence(Utc(2026, 9, 26, 12), Berlin, DayOfWeek.Sunday, Eight));
        // Sunday 07:59 local = 05:59 UTC (summer time)
        Assert.Null(WeeklySchedule.DueOccurrence(Utc(2026, 9, 27, 5, 59), Berlin, DayOfWeek.Sunday, Eight));
    }

    [Fact]
    public void It_is_due_from_the_moment_on_and_stays_due_for_the_rest_of_the_week()
    {
        var expected = Utc(2026, 9, 27, 6); // 08:00 local (UTC+2)
        var atTheMoment = WeeklySchedule.DueOccurrence(Utc(2026, 9, 27, 6), Berlin, DayOfWeek.Sunday, Eight);
        Assert.Equal(new ScheduleOccurrence("2026-W39", expected), atTheMoment);
        // Wednesday's schedule caught up on Friday
        var late = WeeklySchedule.DueOccurrence(Utc(2026, 9, 25, 12), Berlin, DayOfWeek.Wednesday, Eight);
        Assert.Equal(new ScheduleOccurrence("2026-W39", Utc(2026, 9, 23, 6)), late);
    }

    [Fact]
    public void The_weekday_and_the_week_are_taken_in_the_time_zone_of_the_city()
    {
        // Sunday 2026-09-27 23:30 local = 21:30 UTC: still week 39
        Assert.Equal("2026-W39", WeeklySchedule.PeriodKey(Utc(2026, 9, 27, 21, 30), Berlin));
        // 22:30 UTC is Monday 00:30 local: week 40, although UTC still says Sunday
        Assert.Equal("2026-W40", WeeklySchedule.PeriodKey(Utc(2026, 9, 27, 22, 30), Berlin));
        Assert.Equal("2026-W39", WeeklySchedule.PeriodKey(Utc(2026, 9, 27, 22, 30), TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_offset_follows_daylight_saving_time()
    {
        // Sunday 2026-10-25 08:00 local: the clocks went back that night, so UTC+1 (07:00 UTC)
        var winter = WeeklySchedule.DueOccurrence(Utc(2026, 10, 25, 12), Berlin, DayOfWeek.Sunday, Eight);
        Assert.Equal(Utc(2026, 10, 25, 7), winter!.Value.At);
    }

    [Fact]
    public void A_time_that_does_not_exist_moves_to_the_next_hour()
    {
        // 2026-03-29 02:30 does not exist in Berlin (02:00 jumps to 03:00): fires at 03:30 local = 01:30 UTC
        var occurrence = WeeklySchedule.DueOccurrence(Utc(2026, 3, 29, 12), Berlin, DayOfWeek.Sunday, new TimeOnly(2, 30));
        Assert.Equal(Utc(2026, 3, 29, 1, 30), occurrence!.Value.At);
    }

    [Fact]
    public void An_ambiguous_time_counts_once_at_the_earlier_moment()
    {
        // 2026-10-25 02:30 happens twice; the first one is UTC+2 = 00:30 UTC
        var occurrence = WeeklySchedule.DueOccurrence(Utc(2026, 10, 25, 12), Berlin, DayOfWeek.Sunday, new TimeOnly(2, 30));
        Assert.Equal(Utc(2026, 10, 25, 0, 30), occurrence!.Value.At);
    }

    [Fact]
    public void The_week_key_uses_the_iso_year_around_new_year()
    {
        Assert.Equal("2026-W53", WeeklySchedule.PeriodKey(Utc(2027, 1, 1, 12), Berlin)); // Friday, still ISO week 53 of 2026
        Assert.Equal("2027-W01", WeeklySchedule.PeriodKey(Utc(2027, 1, 4, 12), Berlin));
    }

    [Fact]
    public void The_next_occurrence_is_this_week_when_still_ahead_and_otherwise_next_week()
    {
        var ahead = WeeklySchedule.NextOccurrence(Utc(2026, 9, 26, 12), Berlin, DayOfWeek.Sunday, Eight);
        Assert.Equal(new ScheduleOccurrence("2026-W39", Utc(2026, 9, 27, 6)), ahead);
        var passed = WeeklySchedule.NextOccurrence(Utc(2026, 9, 27, 6), Berlin, DayOfWeek.Sunday, Eight); // exactly at the moment
        Assert.Equal(new ScheduleOccurrence("2026-W40", Utc(2026, 10, 4, 6)), passed);
    }

    [Fact]
    public void The_moment_is_given_in_utc_because_the_database_takes_no_other_offset()
        => Assert.Equal(TimeSpan.Zero, WeeklySchedule.DueOccurrence(Utc(2026, 9, 27, 12), Berlin, DayOfWeek.Sunday, Eight)!.Value.At.Offset);
}
