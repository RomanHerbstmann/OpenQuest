using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class LevelCurveTests
{
    [Theory]
    [InlineData(0, 1, 0, 100, 0)]
    [InlineData(99, 1, 99, 100, 99)]
    [InlineData(100, 2, 0, 150, 0)]
    [InlineData(175, 2, 75, 150, 50)]
    [InlineData(249, 2, 149, 150, 99)]
    [InlineData(250, 3, 0, 250, 0)]
    [InlineData(500, 4, 0, 300, 0)]
    public void Progress_within_a_level(int points, int level, int current, int required, int percent)
    {
        var p = LevelCurve.Default.ProgressFor(points);
        Assert.Equal((level, current, required, percent, false), (p.Level, p.Current, p.Required, p.Percent, p.IsMaxLevel));
    }

    [Theory]
    [InlineData(800, 0)]
    [InlineData(1000, 200)]
    public void The_highest_level_is_never_left(int points, int current)
    {
        var p = LevelCurve.Default.ProgressFor(points);
        Assert.Equal((5, current, 0, 100, true), (p.Level, p.Current, p.Required, p.Percent, p.IsMaxLevel));
    }

    [Fact]
    public void Negative_points_count_as_zero()
    {
        Assert.Equal(1, LevelCurve.Default.ProgressFor(-50).Level);
        Assert.Equal(0, LevelCurve.Default.ProgressFor(-50).Current);
    }

    [Fact]
    public void A_city_can_bring_its_own_curve()
    {
        var curve = new LevelCurve([0, 10, 30]);
        Assert.Equal(3, curve.MaxLevel);
        Assert.Equal(2, curve.ProgressFor(10).Level);
        Assert.True(curve.ProgressFor(30).IsMaxLevel);
    }

    [Theory]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 5, 10 })]
    [InlineData(new[] { 0, 10, 10 })]
    [InlineData(new[] { 0, 20, 10 })]
    public void Invalid_curves_are_rejected(int[] thresholds)
        => Assert.Throws<ArgumentException>(() => new LevelCurve(thresholds));
}
