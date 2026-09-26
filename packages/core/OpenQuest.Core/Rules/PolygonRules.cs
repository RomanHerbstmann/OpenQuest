using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Rules;

/// <summary>One thing wrong with a drawn polygon. Edge indices refer to the vertex list without a repeated closing point.</summary>
public sealed record PolygonProblem(string Code, string Message, int? EdgeA = null, int? EdgeB = null, GeoPoint? At = null);

/// <summary>
/// Rules for a district outline given as an ordered list of vertices (the ring closes from the last vertex back to the
/// first). Pure and framework-free. Coordinates are treated as planar lon/lat, which is exact enough at city scale.
/// </summary>
public static class PolygonRules
{
    public const int MinPoints = 3;
    public const int MaxPoints = 5000;
    private const int MaxReportedIntersections = 10;

    /// <summary>Drops a repeated closing vertex (GeoJSON rings repeat the first vertex at the end).</summary>
    public static IReadOnlyList<GeoPoint> Normalize(IReadOnlyList<GeoPoint> points)
        => points.Count > 1 && points[0] == points[^1] ? points.Take(points.Count - 1).ToList() : points.ToList();

    /// <summary>Empty result = the outline is a simple polygon whose edges do not cross or touch each other.</summary>
    public static IReadOnlyList<PolygonProblem> Validate(IReadOnlyList<GeoPoint> input)
    {
        if (input.Count > MaxPoints + 1)
            return [new PolygonProblem("too_many_points", $"At most {MaxPoints} points are allowed.")];

        var p = Normalize(input);
        var problems = new List<PolygonProblem>();

        for (var i = 0; i < p.Count; i++)
            if (!double.IsFinite(p[i].Lat) || !double.IsFinite(p[i].Lon) || p[i].Lat is < -90 or > 90 || p[i].Lon is < -180 or > 180)
                problems.Add(new PolygonProblem("invalid_coordinate", $"Point {i} is not a valid WGS84 position (lat -90..90, lon -180..180).", EdgeA: i));
        if (problems.Count > 0) return problems;

        for (var i = 0; i < p.Count; i++)
            if (p[i] == p[(i + 1) % p.Count] && p.Count > 1)
                problems.Add(new PolygonProblem("duplicate_point", $"Point {i} is repeated directly after itself.", EdgeA: i, At: p[i]));
        if (p.Count < MinPoints)
            return [new PolygonProblem("too_few_points", $"A polygon needs at least {MinPoints} different points.")];
        if (problems.Count > 0) return problems;

        var n = p.Count;
        for (var i = 0; i < n && problems.Count < MaxReportedIntersections; i++)
            for (var j = i + 1; j < n && problems.Count < MaxReportedIntersections; j++)
            {
                var adjacent = j == i + 1 || (i == 0 && j == n - 1);
                if (adjacent) continue;
                if (TryIntersect(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n], out var at))
                    problems.Add(new PolygonProblem("self_intersection", $"Edge {i} and edge {j} cross or touch each other.", i, j, at));
            }

        // Neighbouring edges share a vertex; they must not fold back onto each other (a spike).
        for (var i = 0; i < n && problems.Count < MaxReportedIntersections; i++)
        {
            var (a, b, c) = (p[i], p[(i + 1) % n], p[(i + 2) % n]);
            var straight = Cross(a, b, c) == 0;
            var dot = (b.Lon - a.Lon) * (c.Lon - b.Lon) + (b.Lat - a.Lat) * (c.Lat - b.Lat);
            if (straight && dot < 0)
                problems.Add(new PolygonProblem("self_intersection", $"Edge {i} and edge {(i + 1) % n} fold back onto each other.", i, (i + 1) % n, b));
        }

        if (problems.Count == 0 && Math.Abs(SignedArea(p)) < 1e-14)
            problems.Add(new PolygonProblem("zero_area", "The points lie on a line: the polygon has no area."));
        return problems;
    }

    /// <summary>Area-weighted centre of the polygon (for labels). Falls back to the average of the vertices.</summary>
    public static GeoPoint Centroid(IReadOnlyList<GeoPoint> input)
    {
        var p = Normalize(input);
        if (p.Count == 0) throw new ArgumentException("No points.", nameof(input));
        var (ox, oy) = (p[0].Lon, p[0].Lat); // shift to the first vertex: keeps the arithmetic accurate
        double a2 = 0, cx = 0, cy = 0;
        for (var i = 0; i < p.Count; i++)
        {
            var (x1, y1) = (p[i].Lon - ox, p[i].Lat - oy);
            var (x2, y2) = (p[(i + 1) % p.Count].Lon - ox, p[(i + 1) % p.Count].Lat - oy);
            var cross = x1 * y2 - x2 * y1;
            a2 += cross;
            cx += (x1 + x2) * cross;
            cy += (y1 + y2) * cross;
        }
        if (Math.Abs(a2) < 1e-18) return new GeoPoint(p.Average(x => x.Lat), p.Average(x => x.Lon));
        return new GeoPoint(oy + cy / (3 * a2), ox + cx / (3 * a2));
    }

    /// <summary>Shoelace area in square degrees; positive for counter-clockwise rings.</summary>
    public static double SignedArea(IReadOnlyList<GeoPoint> p)
    {
        double sum = 0;
        for (var i = 0; i < p.Count; i++)
        {
            var (a, b) = (p[i], p[(i + 1) % p.Count]);
            sum += a.Lon * b.Lat - b.Lon * a.Lat;
        }
        return sum / 2;
    }

    private static double Cross(GeoPoint a, GeoPoint b, GeoPoint c)
        => (b.Lon - a.Lon) * (c.Lat - a.Lat) - (b.Lat - a.Lat) * (c.Lon - a.Lon);

    private static bool OnSegment(GeoPoint a, GeoPoint b, GeoPoint p)
        => p.Lon >= Math.Min(a.Lon, b.Lon) && p.Lon <= Math.Max(a.Lon, b.Lon)
        && p.Lat >= Math.Min(a.Lat, b.Lat) && p.Lat <= Math.Max(a.Lat, b.Lat);

    /// <summary>True if the segments share at least one point (crossing, touching or overlapping).</summary>
    private static bool TryIntersect(GeoPoint p1, GeoPoint p2, GeoPoint p3, GeoPoint p4, out GeoPoint at)
    {
        var d1 = Cross(p3, p4, p1);
        var d2 = Cross(p3, p4, p2);
        var d3 = Cross(p1, p2, p3);
        var d4 = Cross(p1, p2, p4);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            var t = d1 / (d1 - d2);
            at = new GeoPoint(p1.Lat + t * (p2.Lat - p1.Lat), p1.Lon + t * (p2.Lon - p1.Lon));
            return true;
        }
        if (d1 == 0 && OnSegment(p3, p4, p1)) { at = p1; return true; }
        if (d2 == 0 && OnSegment(p3, p4, p2)) { at = p2; return true; }
        if (d3 == 0 && OnSegment(p1, p2, p3)) { at = p3; return true; }
        if (d4 == 0 && OnSegment(p1, p2, p4)) { at = p4; return true; }
        at = default;
        return false;
    }
}
