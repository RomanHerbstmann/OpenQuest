namespace OpenQuest.Core.Domain;

/// <summary>The kinds of criteria a badge can have (<see cref="BadgeCriteria.Type"/>).</summary>
public static class BadgeCriteriaTypes
{
    /// <summary>Approved submissions, all task types together.</summary>
    public const string ApprovedSubmissions = "approved_submissions";
    /// <summary>Total points.</summary>
    public const string Points = "points";
    /// <summary>Cards collected.</summary>
    public const string Cards = "cards";
    /// <summary>Different genera among the cards.</summary>
    public const string DistinctGenera = "distinct_genera";
    /// <summary>Cards of at least the given rarity.</summary>
    public const string RarityCards = "rarity_cards";
    /// <summary>Trees reported as missing in the data that were approved.</summary>
    public const string NewTrees = "new_trees";
    /// <summary>Approved submissions of one task type.</summary>
    public const string TaskType = "task_type";

    public static readonly IReadOnlyList<string> All =
        [ApprovedSubmissions, Points, Cards, DistinctGenera, RarityCards, NewTrees, TaskType];
}

/// <summary>
/// What a player has to reach for a badge: <c>Count</c> of the thing <c>Type</c> names. <c>Rarity</c> belongs to
/// <see cref="BadgeCriteriaTypes.RarityCards"/> ("rare", counts rare and legendary cards), <c>TaskType</c> to <see cref="BadgeCriteriaTypes.TaskType"/>.
/// Stored as JSON: <c>{"type":"rarity_cards","rarity":"rare","count":1}</c>.
/// </summary>
public sealed record BadgeCriteria(string Type, int Count, string? Rarity = null, string? TaskType = null);

/// <summary>What a player has done so far, as far as badges care. Calculated from the database; the rules only see this.</summary>
public sealed record UserStats(
    int ApprovedSubmissions, int Points, int Cards, int DistinctGenera,
    IReadOnlyDictionary<Rarity, int> CardsByRarity, int NewTrees, IReadOnlyDictionary<string, int> ApprovedByTaskType)
{
    public static readonly UserStats None = new(0, 0, 0, 0, new Dictionary<Rarity, int>(), 0, new Dictionary<string, int>());
}

/// <summary>How far a player is towards a badge. <c>Current</c> can exceed <c>Required</c>; <c>Percent</c> cannot.</summary>
public readonly record struct BadgeProgress(int Current, int Required)
{
    public bool Met => Current >= Required;
    public int Percent => Required <= 0 ? 100 : Math.Min(100, (int)(100L * Math.Max(0, Current) / Required));
}

/// <summary>
/// A badge of the default catalog. <c>Name</c> and <c>Description</c> are translation keys for the client (like task and asset types).
/// <c>RewardPoints</c> are paid once when the badge is earned.
/// </summary>
public sealed record BadgeDefinition(string Key, string Name, string Description, string Icon, BadgeCriteria Criteria, int RewardPoints);

/// <summary>
/// The badges every deployment starts with. They pay no bonus points, so that they do not skew the leaderboards and levels; an admin can add a bonus, add more
/// badges, change or deactivate them. The catalog only fills in what is missing.
/// </summary>
public static class BadgeCatalog
{
    private static BadgeDefinition B(string key, string icon, BadgeCriteria criteria, int reward)
        => new(key, $"badge.{key}", $"badge.{key}.description", icon, criteria, reward);

    public static readonly IReadOnlyList<BadgeDefinition> Defaults =
    [
        B("first_steps", "footprints", new(BadgeCriteriaTypes.ApprovedSubmissions, 1), 0),
        B("regular", "sprout", new(BadgeCriteriaTypes.ApprovedSubmissions, 10), 0),
        B("veteran", "trees", new(BadgeCriteriaTypes.ApprovedSubmissions, 50), 0),
        B("tree_doctor", "stethoscope", new(BadgeCriteriaTypes.TaskType, 5, TaskType: "condition_report"), 0),
        B("collector", "layers", new(BadgeCriteriaTypes.Cards, 10), 0),
        B("genus_hunter", "leaf", new(BadgeCriteriaTypes.DistinctGenera, 5), 0),
        B("rare_find", "gem", new(BadgeCriteriaTypes.RarityCards, 1, Rarity: "rare"), 0),
        B("legend", "crown", new(BadgeCriteriaTypes.RarityCards, 1, Rarity: "legendary"), 0),
        B("pioneer", "flag", new(BadgeCriteriaTypes.NewTrees, 1), 0),
        B("high_score", "trophy", new(BadgeCriteriaTypes.Points, 500), 0),
    ];
}
