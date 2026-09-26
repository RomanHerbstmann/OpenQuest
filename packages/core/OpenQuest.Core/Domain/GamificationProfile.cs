using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Domain;

/// <summary>
/// The game rules a city can tune. The core ships defaults; a deployment overrides what it needs (configuration), so no
/// city-specific code lives here. Later parts of the game (quest templates) hang off this record.
/// </summary>
public sealed record GamificationProfile(LevelCurve Levels, RarityProfile Rarity)
{
    public static readonly GamificationProfile Default = new(LevelCurve.Default, RarityProfile.Default);
}
