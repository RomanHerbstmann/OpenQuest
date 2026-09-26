using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Domain;

/// <summary>
/// The game rules a city can tune. The core ships defaults; a deployment overrides what it needs (configuration), so no
/// city-specific code lives here. Later parts of the game (rarity, quest templates) hang off this record.
/// </summary>
public sealed record GamificationProfile(LevelCurve Levels)
{
    public static readonly GamificationProfile Default = new(LevelCurve.Default);
}
