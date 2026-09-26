using System.Security.Cryptography;
using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Rules;

/// <param name="Share">Share of the genus among the district's trees with a known genus (0 to 1); null if there are no statistics.</param>
/// <param name="Sample">Number of trees with a known genus in the district.</param>
/// <param name="NewInformation">The approved submission found out something the data did not have (for example the genus of a tree without one).</param>
/// <param name="ConditionFact">The approved submission reported a problem with the tree (damaged, dead, gone).</param>
public sealed record RarityInput(double? Share, int Sample, bool NewInformation, bool ConditionFact);

/// <param name="Frequency">How frequent the genus is in the district.</param>
/// <param name="Boosted">The frequency after the boosts for new information and facts; the rarity was rolled with this one.</param>
/// <param name="Reasons">Why the chances were what they were: <c>scarce_in_district</c>, <c>new_to_district</c>, <c>new_information</c>, <c>condition_fact</c>.</param>
public sealed record RarityResult(Rarity Rarity, Frequency Frequency, Frequency Boosted, IReadOnlyList<string> Reasons);

/// <summary>
/// Which rarity a collected card has. Trees that are common in the district give mostly common cards; scarce trees, and new
/// findings, have better chances of rare ones. The dice roll is derived from the submission, so a redelivered event gives the same card.
/// Pure and framework-free.
/// </summary>
public static class CardRarity
{
    public static Frequency Classify(double? share, int sample, RarityProfile profile)
    {
        if (sample < profile.MinSample) return Frequency.Common;   // too little data to call anything scarce
        var s = share ?? 0;
        if (s >= profile.AbundantShare) return Frequency.Abundant;
        if (s >= profile.CommonShare) return Frequency.Common;
        if (s >= profile.ScarceShare) return Frequency.Scarce;
        return Frequency.VeryScarce;
    }

    /// <param name="roll">A number in [0, 1), see <see cref="RollFor"/>.</param>
    public static RarityResult Decide(RarityInput input, double roll, RarityProfile profile)
    {
        var frequency = Classify(input.Share, input.Sample, profile);
        var boost = (input.NewInformation ? profile.NewInformationBoost : 0) + (input.ConditionFact ? profile.ConditionFactBoost : 0);
        var boosted = (Frequency)Math.Min((int)Frequency.VeryScarce, (int)frequency + boost);

        var reasons = new List<string>();
        var reliable = input.Sample >= profile.MinSample;
        if (reliable && (input.Share ?? 0) == 0) reasons.Add("new_to_district");
        else if (frequency >= Frequency.Scarce) reasons.Add("scarce_in_district");
        if (input.NewInformation) reasons.Add("new_information");
        if (input.ConditionFact) reasons.Add("condition_fact");

        return new RarityResult(Pick(profile.Weights[boosted], roll), frequency, boosted, reasons);
    }

    private static Rarity Pick(RarityWeights w, double roll)
    {
        var r = Math.Clamp(roll, 0, 1 - 1e-12);
        if (r < w.Common) return Rarity.Common;
        if (r < w.Common + w.Uncommon) return Rarity.Uncommon;
        if (r < w.Common + w.Uncommon + w.Rare) return Rarity.Rare;
        return Rarity.Legendary;
    }

    /// <summary>A number in [0, 1) that only depends on <paramref name="seed"/> (for example the submission id).</summary>
    public static double RollFor(Guid seed)
    {
        var hash = SHA256.HashData(seed.ToByteArray());
        return (BitConverter.ToUInt64(hash, 0) >> 11) / (double)(1UL << 53);
    }
}
