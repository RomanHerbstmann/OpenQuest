using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class PolygonRulesTests
{
    private static GeoPoint P(double lat, double lon) => new(lat, lon);

    private static readonly GeoPoint[] Square = [P(0, 0), P(0, 1), P(1, 1), P(1, 0)];

    private static string[] Codes(IReadOnlyList<GeoPoint> points) => PolygonRules.Validate(points).Select(p => p.Code).ToArray();

    [Fact]
    public void A_square_and_a_triangle_are_valid_in_either_direction()
    {
        Assert.Empty(PolygonRules.Validate(Square));
        Assert.Empty(PolygonRules.Validate(Square.Reverse().ToArray()));
        Assert.Empty(PolygonRules.Validate([P(0, 0), P(0, 2), P(2, 1)]));
    }

    [Fact]
    public void A_repeated_closing_point_is_dropped_and_not_a_duplicate()
    {
        var closed = Square.Append(Square[0]).ToArray();
        Assert.Empty(PolygonRules.Validate(closed));
        Assert.Equal(4, PolygonRules.Normalize(closed).Count);
    }

    [Fact]
    public void A_concave_polygon_is_valid()
        // a "C" opening to the right
        => Assert.Empty(PolygonRules.Validate([P(0, 0), P(3, 0), P(3, 3), P(2, 3), P(2, 1), P(1, 1), P(1, 3), P(0, 3)]));

    [Fact]
    public void A_bow_tie_is_rejected_with_the_crossing_edges_and_the_crossing_point()
    {
        // (0,0) -> (1,1) -> (0,1) -> (1,0): the first and third edge cross in the middle
        var problems = PolygonRules.Validate([P(0, 0), P(1, 1), P(1, 0), P(0, 1)]);
        var problem = Assert.Single(problems);
        Assert.Equal("self_intersection", problem.Code);
        Assert.Equal((0, 2), (problem.EdgeA, problem.EdgeB));
        Assert.Equal(0.5, problem.At!.Value.Lat, 9);
        Assert.Equal(0.5, problem.At!.Value.Lon, 9);
    }

    [Fact]
    public void Order_matters_the_same_points_in_another_order_can_cross()
    {
        GeoPoint[] good = [P(0, 0), P(0, 1), P(1, 1), P(1, 0)];
        GeoPoint[] crossing = [P(0, 0), P(1, 1), P(0, 1), P(1, 0)];
        Assert.Empty(PolygonRules.Validate(good));
        Assert.Contains("self_intersection", Codes(crossing));
    }

    [Fact]
    public void Touching_itself_at_a_vertex_is_rejected()
    {
        // an hourglass whose two halves meet in a point that is also a vertex
        Assert.Contains("self_intersection", Codes([P(0, 0), P(2, 0), P(1, 1), P(2, 2), P(0, 2), P(1, 1)]));
    }

    [Fact]
    public void An_edge_folding_back_onto_the_previous_one_is_rejected()
        => Assert.Contains("self_intersection", Codes([P(0, 0), P(0, 2), P(0, 1), P(1, 1)]));

    [Fact]
    public void Too_few_points_and_duplicates_are_rejected()
    {
        Assert.Equal(["too_few_points"], Codes([P(0, 0), P(1, 1)]));
        Assert.Equal(["too_few_points"], Codes([]));
        Assert.Contains("duplicate_point", Codes([P(0, 0), P(0, 1), P(0, 1), P(1, 1)]));
    }

    [Fact]
    public void Points_on_a_line_have_no_area()
        => Assert.Contains(Codes([P(0, 0), P(0, 1), P(0, 2)]), c => c is "zero_area" or "self_intersection");

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public void Coordinates_out_of_range_are_rejected(double lat, double lon)
        => Assert.Equal(["invalid_coordinate"], Codes([P(lat, lon), P(0, 1), P(1, 1)]));

    [Fact]
    public void Too_many_points_are_rejected()
    {
        var many = Enumerable.Range(0, PolygonRules.MaxPoints + 5).Select(i => P(0, i * 1e-6)).ToArray();
        Assert.Equal(["too_many_points"], Codes(many));
    }

    [Fact]
    public void A_large_ring_of_points_is_valid_and_fast()
    {
        const int n = 2000;
        var ring = Enumerable.Range(0, n).Select(i => P(51.96 + 0.01 * Math.Sin(2 * Math.PI * i / n), 7.62 + 0.02 * Math.Cos(2 * Math.PI * i / n))).ToArray();
        Assert.Empty(PolygonRules.Validate(ring));
    }

    [Fact]
    public void Centroid_of_a_rectangle_is_its_middle_whatever_the_direction_or_start()
    {
        GeoPoint[] rect = [P(51.0, 7.0), P(51.0, 7.4), P(51.2, 7.4), P(51.2, 7.0)];
        foreach (var ring in new[] { rect, rect.Reverse().ToArray(), rect.Skip(2).Concat(rect.Take(2)).ToArray() })
        {
            var c = PolygonRules.Centroid(ring);
            Assert.Equal(51.1, c.Lat, 9);
            Assert.Equal(7.2, c.Lon, 9);
        }
    }

    [Fact]
    public void Centroid_of_an_l_shape_is_pulled_towards_the_heavy_side()
    {
        var c = PolygonRules.Centroid([P(0, 0), P(0, 4), P(1, 4), P(1, 1), P(4, 1), P(4, 0)]);
        Assert.InRange(c.Lon, 1.2, 1.9);
        Assert.InRange(c.Lat, 1.2, 1.9);
    }

    [Fact]
    public void Signed_area_is_positive_counter_clockwise()
    {
        Assert.Equal(1, PolygonRules.SignedArea(Square), 9);
        Assert.Equal(-1, PolygonRules.SignedArea(Square.Reverse().ToArray()), 9);
    }
}
