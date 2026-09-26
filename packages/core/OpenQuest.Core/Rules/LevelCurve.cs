namespace OpenQuest.Core.Rules;

/// <summary>Where a player stands within the current level.</summary>
/// <param name="Level">1-based level.</param>
/// <param name="Current">Points collected within this level.</param>
/// <param name="Required">Points needed to finish this level (0 at the highest level).</param>
/// <param name="Percent">0 to 100, progress within this level (100 at the highest level).</param>
public sealed record LevelProgress(int Level, int Current, int Required, int Percent, bool IsMaxLevel);

/// <summary>
/// Maps total points to levels. <c>thresholds[i]</c> is the number of points at which level i + 1 starts, so the first
/// entry is 0. A city can bring its own curve (configuration); <see cref="Default"/> is used otherwise.
/// </summary>
public sealed class LevelCurve
{
    public static readonly LevelCurve Default = new([0, 100, 250, 500, 800]);

    private readonly int[] _thresholds;

    public LevelCurve(IEnumerable<int> thresholds)
    {
        _thresholds = thresholds.ToArray();
        if (_thresholds.Length < 2) throw new ArgumentException("A level curve needs at least two levels.", nameof(thresholds));
        if (_thresholds[0] != 0) throw new ArgumentException("The first level starts at 0 points.", nameof(thresholds));
        for (var i = 1; i < _thresholds.Length; i++)
            if (_thresholds[i] <= _thresholds[i - 1]) throw new ArgumentException("Thresholds must be strictly increasing.", nameof(thresholds));
    }

    public IReadOnlyList<int> Thresholds => _thresholds;

    public int MaxLevel => _thresholds.Length;

    public LevelProgress ProgressFor(int totalPoints)
    {
        var points = Math.Max(0, totalPoints);
        var level = 1;
        while (level < MaxLevel && points >= _thresholds[level]) level++;

        var current = points - _thresholds[level - 1];
        if (level == MaxLevel) return new LevelProgress(level, current, 0, 100, IsMaxLevel: true);

        var required = _thresholds[level] - _thresholds[level - 1];
        return new LevelProgress(level, current, required, (int)(current * 100L / required), IsMaxLevel: false);
    }
}
