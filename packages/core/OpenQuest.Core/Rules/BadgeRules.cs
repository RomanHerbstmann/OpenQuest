using System.Text.Json;
using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Rules;

/// <summary>Pure rules of the badges: what a criteria means, whether it is valid, and how far a player is.</summary>
public static class BadgeRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions JsonWithoutNulls = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Problems with a criteria (empty when it is valid): unknown type, count out of range, missing or unknown rarity / task type.</summary>
    public static IReadOnlyList<string> Validate(BadgeCriteria c)
    {
        var problems = new List<string>();
        if (!BadgeCriteriaTypes.All.Contains(c.Type)) problems.Add($"Unknown type '{c.Type}' (one of: {string.Join(", ", BadgeCriteriaTypes.All)}).");
        if (c.Count is < 1 or > 1_000_000) problems.Add("count must be between 1 and 1000000.");
        if (c.Type == BadgeCriteriaTypes.RarityCards && !TryParseRarity(c.Rarity, out _)) problems.Add("rarity is required for rarity_cards: common, uncommon, rare or legendary.");
        if (c.Type != BadgeCriteriaTypes.RarityCards && c.Rarity is not null) problems.Add("rarity only belongs to rarity_cards.");
        if (c.Type == BadgeCriteriaTypes.TaskType && !TaskTypes.TryParse(c.TaskType, out _)) problems.Add("taskType is required for task_type and must be a known task type.");
        if (c.Type != BadgeCriteriaTypes.TaskType && c.TaskType is not null) problems.Add("taskType only belongs to task_type.");
        return problems;
    }

    /// <summary>How far the player is. An invalid criteria never counts as met.</summary>
    public static BadgeProgress Progress(BadgeCriteria c, UserStats stats)
    {
        if (Validate(c).Count > 0) return new BadgeProgress(0, Math.Max(1, c.Count));
        var current = c.Type switch
        {
            BadgeCriteriaTypes.ApprovedSubmissions => stats.ApprovedSubmissions,
            BadgeCriteriaTypes.Points => stats.Points,
            BadgeCriteriaTypes.Cards => stats.Cards,
            BadgeCriteriaTypes.DistinctGenera => stats.DistinctGenera,
            BadgeCriteriaTypes.NewTrees => stats.NewTrees,
            BadgeCriteriaTypes.TaskType => stats.ApprovedByTaskType.GetValueOrDefault(TaskTypes.TryParse(c.TaskType, out var t) ? t.Key() : ""),
            // a legendary card is at least as good as a rare one
            BadgeCriteriaTypes.RarityCards when TryParseRarity(c.Rarity, out var min) => stats.CardsByRarity.Where(kv => kv.Key >= min).Sum(kv => kv.Value),
            _ => 0,
        };
        return new BadgeProgress(current, c.Count);
    }

    public static bool IsMet(BadgeCriteria c, UserStats stats) => Progress(c, stats).Met;

    public static bool TryParseRarity(string? text, out Rarity rarity)
    {
        foreach (var r in Enum.GetValues<Rarity>())
            if (string.Equals(r.ToString(), text, StringComparison.OrdinalIgnoreCase)) { rarity = r; return true; }
        rarity = default;
        return false;
    }

    public static string ToJson(BadgeCriteria c) => JsonSerializer.Serialize(c, JsonWithoutNulls);

    /// <summary>Reads a stored criteria; null if the JSON is not one.</summary>
    public static BadgeCriteria? FromJson(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<BadgeCriteria>(json, Json); }
        catch (JsonException) { return null; }
    }
}
