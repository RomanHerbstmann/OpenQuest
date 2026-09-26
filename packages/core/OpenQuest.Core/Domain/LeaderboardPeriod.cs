namespace OpenQuest.Core.Domain;

/// <summary>Time window of a leaderboard.</summary>
public enum LeaderboardPeriod
{
    /// <summary>Everything since the start.</summary>
    All,
    /// <summary>The current calendar week, Monday 00:00 to Sunday, in the city's time zone.</summary>
    Week,
}
