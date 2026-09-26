using System.Text.Json.Nodes;

namespace OpenQuest.Core.Rules;

/// <summary>Where a piece of data comes from. Open data is what the city delivered (the importer); user data is what players contributed and a moderator accepted.</summary>
public static class DataOrigin
{
    public const string OpenData = "open_data";
    public const string User = "user";
}

/// <param name="Value">The value the game shows.</param>
/// <param name="Origin"><see cref="DataOrigin"/> of that value.</param>
/// <param name="ContributionOutdated">A user contribution exists but the city changed the attribute after it, so the city's value wins.</param>
public sealed record ResolvedAttribute(JsonNode? Value, string Origin, bool ContributionOutdated);

/// <summary>
/// Which value of an attribute the game shows: the city's (open data) or a player's accepted contribution. The asset keeps only the city's values;
/// contributions are a separate layer (<c>attribute_change</c>), so it stays clear where every value comes from and an import never overwrites what players
/// found out (nor the other way round).
/// </summary>
public static class AttributeProvenance
{
    /// <summary>
    /// The latest accepted contribution wins, unless
    /// <list type="bullet">
    /// <item>the city has the same value now (then it is simply open data), or</item>
    /// <item>the city's value is not the one the player replaced: the city changed the attribute after the contribution, and the city's newer value wins (<c>ContributionOutdated</c>).</item>
    /// </list>
    /// </summary>
    /// <param name="openData">The city's value now (null = missing or empty).</param>
    /// <param name="contributed">The latest accepted user value; null with <paramref name="hasContribution"/> false = none.</param>
    /// <param name="replacedByContribution">The city's value at the time the player contributed.</param>
    public static ResolvedAttribute Resolve(JsonNode? openData, bool hasContribution, JsonNode? contributed, JsonNode? replacedByContribution)
    {
        if (!hasContribution) return new ResolvedAttribute(openData, DataOrigin.OpenData, false);
        if (JsonNode.DeepEquals(openData, contributed)) return new ResolvedAttribute(openData, DataOrigin.OpenData, false);
        if (!JsonNode.DeepEquals(openData, replacedByContribution)) return new ResolvedAttribute(openData, DataOrigin.OpenData, true);
        return new ResolvedAttribute(contributed, DataOrigin.User, false);
    }
}
