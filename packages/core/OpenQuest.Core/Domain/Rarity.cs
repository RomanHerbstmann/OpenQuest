namespace OpenQuest.Core.Domain;

/// <summary>How rare a collected card is. The higher, the less likely.</summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Legendary,
}

/// <summary>How frequent a genus is among the trees of a district. The scarcer the genus, the better the chances for a rare card.</summary>
public enum Frequency
{
    /// <summary>A large part of the district's trees (for example the lime trees of Münster).</summary>
    Abundant,
    Common,
    Scarce,
    /// <summary>Hardly any tree of that genus in the district, or none at all: the player found something new there.</summary>
    VeryScarce,
}

/// <summary>Chances of the four rarities for one <see cref="Frequency"/>. The four values add up to 1.</summary>
public sealed record RarityWeights(double Common, double Uncommon, double Rare, double Legendary)
{
    public double Sum => Common + Uncommon + Rare + Legendary;
}

/// <summary>
/// The tunable numbers of the card rarity rule. A city (deployment) can override them; the defaults are used otherwise.
/// </summary>
/// <param name="AbundantShare">A genus with at least this share of the district's trees is <see cref="Frequency.Abundant"/>.</param>
/// <param name="CommonShare">At least this share: <see cref="Frequency.Common"/>.</param>
/// <param name="ScarceShare">At least this share: <see cref="Frequency.Scarce"/>; below (or not present): <see cref="Frequency.VeryScarce"/>.</param>
/// <param name="MinSample">Fewer trees with a known genus than this in the district: the statistics are not reliable and count as <see cref="Frequency.Common"/>.</param>
/// <param name="Weights">Chances of each rarity per frequency class.</param>
/// <param name="NewInformationBoost">Classes a genus moves towards scarce when the submission delivered new information (an unknown genus was found out).</param>
/// <param name="ConditionFactBoost">Classes it moves when the submission reported a problem with the tree (damaged, dead, gone).</param>
public sealed record RarityProfile(
    double AbundantShare, double CommonShare, double ScarceShare, int MinSample,
    IReadOnlyDictionary<Frequency, RarityWeights> Weights, int NewInformationBoost, int ConditionFactBoost)
{
    public static readonly RarityProfile Default = new(
        AbundantShare: 0.05, CommonShare: 0.01, ScarceShare: 0.002, MinSample: 30,
        Weights: new Dictionary<Frequency, RarityWeights>
        {
            [Frequency.Abundant] = new(0.85, 0.13, 0.02, 0.00),
            [Frequency.Common] = new(0.60, 0.30, 0.09, 0.01),
            [Frequency.Scarce] = new(0.30, 0.40, 0.25, 0.05),
            [Frequency.VeryScarce] = new(0.10, 0.30, 0.40, 0.20),
        },
        NewInformationBoost: 1, ConditionFactBoost: 1);

    /// <summary>Throws if the numbers cannot work (thresholds out of order, weights that are not chances).</summary>
    public RarityProfile Validated()
    {
        if (!(1 > AbundantShare && AbundantShare > CommonShare && CommonShare > ScarceShare && ScarceShare >= 0))
            throw new ArgumentException("Shares must satisfy 1 > abundant > common > scarce >= 0.");
        if (MinSample < 0) throw new ArgumentException("MinSample must not be negative.");
        if (NewInformationBoost < 0 || ConditionFactBoost < 0) throw new ArgumentException("Boosts must not be negative.");
        foreach (var f in Enum.GetValues<Frequency>())
        {
            if (!Weights.TryGetValue(f, out var w)) throw new ArgumentException($"Weights for {f} are missing.");
            if (w.Common < 0 || w.Uncommon < 0 || w.Rare < 0 || w.Legendary < 0 || Math.Abs(w.Sum - 1) > 1e-6)
                throw new ArgumentException($"Weights for {f} must not be negative and must add up to 1.");
        }
        return this;
    }
}
