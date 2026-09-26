using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Rules;

public static class LeaderboardWindow
{
    public static bool TryParse(string? text, out LeaderboardPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(text)) return true; // default: all
        foreach (var v in Enum.GetValues<LeaderboardPeriod>())
            if (string.Equals(v.ToString(), text, StringComparison.OrdinalIgnoreCase)) { period = v; return true; }
        return false;
    }

    /// <summary>Start of the window (inclusive), or null for "everything". The week starts on Monday at 00:00 in <paramref name="zone"/>.</summary>
    public static DateTimeOffset? StartOf(LeaderboardPeriod period, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (period == LeaderboardPeriod.All) return null;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var monday = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        return new DateTimeOffset(monday, zone.GetUtcOffset(monday)).ToUniversalTime();
    }
}
