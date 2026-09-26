using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class CardRarityTests
{
    private static readonly RarityProfile P = RarityProfile.Default;

    [Theory]
    [InlineData(0.24, Frequency.Abundant)]    // like Tilia in Münster
    [InlineData(0.05, Frequency.Abundant)]
    [InlineData(0.049, Frequency.Common)]
    [InlineData(0.01, Frequency.Common)]
    [InlineData(0.009, Frequency.Scarce)]
    [InlineData(0.002, Frequency.Scarce)]
    [InlineData(0.0019, Frequency.VeryScarce)]
    [InlineData(0.0, Frequency.VeryScarce)]
    public void A_genus_is_classified_by_its_share_of_the_districts_trees(double share, Frequency expected)
        => Assert.Equal(expected, CardRarity.Classify(share, sample: 500, P));

    [Fact]
    public void A_genus_that_is_not_in_the_district_at_all_is_very_scarce()
        => Assert.Equal(Frequency.VeryScarce, CardRarity.Classify(null, sample: 500, P));

    [Fact]
    public void With_too_few_known_trees_nothing_counts_as_scarce()
    {
        Assert.Equal(Frequency.Common, CardRarity.Classify(0.0, sample: P.MinSample - 1, P));
        Assert.Equal(Frequency.Common, CardRarity.Classify(null, sample: 0, P));
        Assert.Equal(Frequency.VeryScarce, CardRarity.Classify(0.0, sample: P.MinSample, P));
    }

    [Fact]
    public void The_default_profile_is_valid_and_every_row_adds_up_to_one()
    {
        P.Validated();
        foreach (var f in Enum.GetValues<Frequency>()) Assert.Equal(1.0, P.Weights[f].Sum, 9);
    }

    [Fact]
    public void Scarcer_genera_have_better_chances_of_rare_cards()
    {
        // the chance of at least "rare" grows from abundant to very scarce
        var chances = Enum.GetValues<Frequency>().Select(f => P.Weights[f].Rare + P.Weights[f].Legendary).ToArray();
        Assert.Equal(chances.Order(), chances);
        Assert.True(chances[^1] > 10 * chances[0]);
        Assert.Equal(0, P.Weights[Frequency.Abundant].Legendary); // an ordinary tree never gives a legendary card
    }

    [Theory]
    [InlineData(0.00, Rarity.Common)]
    [InlineData(0.84, Rarity.Common)]
    [InlineData(0.85, Rarity.Uncommon)]
    [InlineData(0.97, Rarity.Uncommon)]
    [InlineData(0.98, Rarity.Rare)]
    [InlineData(0.999999, Rarity.Rare)]
    public void The_roll_picks_the_rarity_from_the_weights_of_the_frequency(double roll, Rarity expected)
    {
        var result = CardRarity.Decide(new RarityInput(0.24, 500, false, false), roll, P);
        Assert.Equal(expected, result.Rarity);
        Assert.Equal(Frequency.Abundant, result.Frequency);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void New_information_and_facts_move_the_genus_towards_scarce_and_are_named()
    {
        var plain = CardRarity.Decide(new RarityInput(0.24, 500, false, false), 0.5, P);
        var info = CardRarity.Decide(new RarityInput(0.24, 500, true, false), 0.5, P);
        var both = CardRarity.Decide(new RarityInput(0.24, 500, true, true), 0.5, P);

        Assert.Equal(Frequency.Abundant, plain.Boosted);
        Assert.Equal((Frequency.Abundant, Frequency.Common), (info.Frequency, info.Boosted));
        Assert.Equal(Frequency.Scarce, both.Boosted);
        Assert.Equal(["new_information"], info.Reasons);
        Assert.Equal(["new_information", "condition_fact"], both.Reasons);
    }

    [Fact]
    public void Boosts_stop_at_the_scarcest_class()
    {
        var r = CardRarity.Decide(new RarityInput(0.001, 500, true, true), 0.5, P);
        Assert.Equal(Frequency.VeryScarce, r.Boosted);
    }

    [Fact]
    public void A_genus_new_to_the_district_and_a_scarce_one_are_told_apart()
    {
        Assert.Equal(["new_to_district"], CardRarity.Decide(new RarityInput(null, 500, false, false), 0.5, P).Reasons);
        Assert.Equal(["new_to_district"], CardRarity.Decide(new RarityInput(0.0, 500, false, false), 0.5, P).Reasons);
        Assert.Equal(["scarce_in_district"], CardRarity.Decide(new RarityInput(0.005, 500, false, false), 0.5, P).Reasons);
        Assert.Empty(CardRarity.Decide(new RarityInput(0.005, 5, false, false), 0.5, P).Reasons); // too few trees to say
    }

    [Fact]
    public void The_roll_of_a_submission_is_stable_and_within_range()
    {
        var id = Guid.NewGuid();
        Assert.Equal(CardRarity.RollFor(id), CardRarity.RollFor(id));
        foreach (var _ in Enumerable.Range(0, 200)) Assert.InRange(CardRarity.RollFor(Guid.NewGuid()), 0.0, 0.9999999999);
    }

    [Fact]
    public void Rolls_are_evenly_spread_so_the_weights_are_what_players_get()
    {
        const int n = 20_000;
        var rarities = Enumerable.Range(0, n)
            .Select(i => CardRarity.Decide(new RarityInput(0.0005, 500, false, false), CardRarity.RollFor(new Guid(i, 0, 0, [1, 2, 3, 4, 5, 6, 7, 8])), P).Rarity)
            .GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count() / (double)n);
        var w = P.Weights[Frequency.VeryScarce];
        Assert.Equal(w.Common, rarities[Rarity.Common], 1);
        Assert.Equal(w.Uncommon, rarities[Rarity.Uncommon], 1);
        Assert.Equal(w.Rare, rarities[Rarity.Rare], 1);
        Assert.Equal(w.Legendary, rarities[Rarity.Legendary], 1);
    }

    [Fact]
    public void Broken_profiles_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => (P with { CommonShare = 0.5 }).Validated());                       // thresholds out of order
        Assert.Throws<ArgumentException>(() => (P with { MinSample = -1 }).Validated());
        Assert.Throws<ArgumentException>(() => (P with { NewInformationBoost = -1 }).Validated());
        var bad = P.Weights.ToDictionary(kv => kv.Key, kv => kv.Value);
        bad[Frequency.Common] = new RarityWeights(0.5, 0.5, 0.5, 0);                                                // sums to 1.5
        Assert.Throws<ArgumentException>(() => (P with { Weights = bad }).Validated());
        bad.Remove(Frequency.Common);
        Assert.Throws<ArgumentException>(() => (P with { Weights = bad }).Validated());
    }
}
