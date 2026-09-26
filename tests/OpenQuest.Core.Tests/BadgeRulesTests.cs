using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class BadgeRulesTests
{
    private static UserStats Stats(int approved = 0, int points = 0, int cards = 0, int genera = 0, int newTrees = 0,
        Dictionary<Rarity, int>? rarity = null, Dictionary<string, int>? byTask = null)
        => new(approved, points, cards, genera, rarity ?? new(), newTrees, byTask ?? new());

    [Theory]
    [InlineData(BadgeCriteriaTypes.ApprovedSubmissions, 3, 2, false)]
    [InlineData(BadgeCriteriaTypes.ApprovedSubmissions, 3, 3, true)]
    [InlineData(BadgeCriteriaTypes.ApprovedSubmissions, 3, 40, true)]
    public void A_count_criteria_is_met_from_the_count_on(string type, int required, int has, bool met)
        => Assert.Equal(met, BadgeRules.IsMet(new BadgeCriteria(type, required), Stats(approved: has)));

    [Fact]
    public void Each_type_looks_at_its_own_number()
    {
        var stats = Stats(approved: 4, points: 700, cards: 6, genera: 3, newTrees: 2);
        Assert.Equal(4, BadgeRules.Progress(new(BadgeCriteriaTypes.ApprovedSubmissions, 10), stats).Current);
        Assert.Equal(700, BadgeRules.Progress(new(BadgeCriteriaTypes.Points, 500), stats).Current);
        Assert.Equal(6, BadgeRules.Progress(new(BadgeCriteriaTypes.Cards, 10), stats).Current);
        Assert.Equal(3, BadgeRules.Progress(new(BadgeCriteriaTypes.DistinctGenera, 5), stats).Current);
        Assert.Equal(2, BadgeRules.Progress(new(BadgeCriteriaTypes.NewTrees, 1), stats).Current);
    }

    [Fact]
    public void A_legendary_card_counts_for_a_rare_card_but_not_the_other_way_round()
    {
        var stats = Stats(rarity: new() { [Rarity.Common] = 9, [Rarity.Uncommon] = 2, [Rarity.Rare] = 1, [Rarity.Legendary] = 1 });
        Assert.Equal(2, BadgeRules.Progress(new(BadgeCriteriaTypes.RarityCards, 3, Rarity: "rare"), stats).Current);
        Assert.Equal(1, BadgeRules.Progress(new(BadgeCriteriaTypes.RarityCards, 1, Rarity: "Legendary"), stats).Current);   // case does not matter
        Assert.Equal(13, BadgeRules.Progress(new(BadgeCriteriaTypes.RarityCards, 1, Rarity: "common"), stats).Current);
    }

    [Fact]
    public void A_task_type_criteria_counts_only_that_task_type()
    {
        var stats = Stats(approved: 8, byTask: new() { ["condition_report"] = 3, ["photo"] = 5 });
        var progress = BadgeRules.Progress(new(BadgeCriteriaTypes.TaskType, 5, TaskType: "condition_report"), stats);
        Assert.Equal(3, progress.Current);
        Assert.False(progress.Met);
        Assert.Equal(60, progress.Percent);
    }

    [Fact]
    public void The_percentage_never_exceeds_one_hundred_and_handles_nothing_done()
    {
        Assert.Equal(100, new BadgeProgress(250, 100).Percent);
        Assert.Equal(0, new BadgeProgress(0, 100).Percent);
        Assert.Equal(33, new BadgeProgress(1, 3).Percent);
    }

    [Theory]
    [InlineData("nonsense", 1, null, null)]
    [InlineData(BadgeCriteriaTypes.Cards, 0, null, null)]
    [InlineData(BadgeCriteriaTypes.Cards, 1, "rare", null)]                       // rarity does not belong here
    [InlineData(BadgeCriteriaTypes.RarityCards, 1, null, null)]                   // rarity missing
    [InlineData(BadgeCriteriaTypes.RarityCards, 1, "mythic", null)]
    [InlineData(BadgeCriteriaTypes.TaskType, 1, null, null)]                      // task type missing
    [InlineData(BadgeCriteriaTypes.TaskType, 1, null, "dance")]
    [InlineData(BadgeCriteriaTypes.Points, 1, null, "photo")]                     // task type does not belong here
    public void Invalid_criteria_are_reported_and_never_met(string type, int count, string? rarity, string? taskType)
    {
        var criteria = new BadgeCriteria(type, count, rarity, taskType);
        Assert.NotEmpty(BadgeRules.Validate(criteria));
        Assert.False(BadgeRules.IsMet(criteria, Stats(approved: 1000, points: 1000, cards: 1000, genera: 1000, newTrees: 1000,
            rarity: new() { [Rarity.Legendary] = 1000 }, byTask: new() { ["photo"] = 1000 })));
    }

    [Fact]
    public void Criteria_survive_json_and_bad_json_is_not_a_criteria()
    {
        var criteria = new BadgeCriteria(BadgeCriteriaTypes.RarityCards, 2, Rarity: "rare");
        var json = BadgeRules.ToJson(criteria);
        Assert.Equal("""{"type":"rarity_cards","count":2,"rarity":"rare"}""", json);   // unset parts are left out
        Assert.Equal(criteria, BadgeRules.FromJson(json));
        Assert.Null(BadgeRules.FromJson("not json"));
        Assert.Null(BadgeRules.FromJson(null));
    }

    [Fact]
    public void The_default_catalog_is_valid_unique_and_pays_no_bonus()
    {
        Assert.Equal(BadgeCatalog.Defaults.Count, BadgeCatalog.Defaults.Select(b => b.Key).Distinct().Count());
        foreach (var badge in BadgeCatalog.Defaults)
        {
            Assert.Empty(BadgeRules.Validate(badge.Criteria));
            Assert.Equal($"badge.{badge.Key}", badge.Name);
            Assert.Equal($"badge.{badge.Key}.description", badge.Description);
            Assert.Equal(0, badge.RewardPoints);
        }
    }
}
