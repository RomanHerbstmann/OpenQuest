using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class LeaderboardWindowTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void All_has_no_start()
        => Assert.Null(LeaderboardWindow.StartOf(LeaderboardPeriod.All, DateTimeOffset.UtcNow, Berlin));

    [Fact]
    public void The_week_starts_on_monday_midnight_in_the_city_time_zone()
    {
        // Friday 2026-09-25 12:00 UTC, summer time (UTC+2): Monday 2026-09-21 00:00 local = Sunday 22:00 UTC
        var start = LeaderboardWindow.StartOf(LeaderboardPeriod.Week, new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero), Berlin);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 22, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void Sunday_evening_still_belongs_to_the_week_that_is_ending()
    {
        // Sunday 2026-09-27 23:30 local
        var start = LeaderboardWindow.StartOf(LeaderboardPeriod.Week, new DateTimeOffset(2026, 9, 27, 21, 30, 0, TimeSpan.Zero), Berlin);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 22, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void Just_after_midnight_local_time_on_monday_a_new_week_has_begun()
    {
        // Monday 2026-09-28 00:30 local = Sunday 22:30 UTC
        var start = LeaderboardWindow.StartOf(LeaderboardPeriod.Week, new DateTimeOffset(2026, 9, 27, 22, 30, 0, TimeSpan.Zero), Berlin);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void Winter_time_uses_its_own_offset()
    {
        // Wednesday 2026-01-14 10:00 UTC, winter time (UTC+1): Monday 2026-01-12 00:00 local = Sunday 23:00 UTC
        var start = LeaderboardWindow.StartOf(LeaderboardPeriod.Week, new DateTimeOffset(2026, 1, 14, 10, 0, 0, TimeSpan.Zero), Berlin);
        Assert.Equal(new DateTimeOffset(2026, 1, 11, 23, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void The_week_across_the_switch_to_winter_time_starts_at_the_summer_offset()
    {
        // Clocks go back on Sunday 2026-10-25. Tuesday 2026-10-27 12:00 UTC: Monday 2026-10-26 00:00 local is already winter time.
        var start = LeaderboardWindow.StartOf(LeaderboardPeriod.Week, new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero), Berlin);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), start);
    }

    [Theory]
    [InlineData(null, true, LeaderboardPeriod.All)]
    [InlineData("all", true, LeaderboardPeriod.All)]
    [InlineData("WEEK", true, LeaderboardPeriod.Week)]
    [InlineData("month", false, LeaderboardPeriod.All)]
    public void Parsing(string? text, bool ok, LeaderboardPeriod expected)
    {
        Assert.Equal(ok, LeaderboardWindow.TryParse(text, out var period));
        if (ok) Assert.Equal(expected, period);
    }
}
