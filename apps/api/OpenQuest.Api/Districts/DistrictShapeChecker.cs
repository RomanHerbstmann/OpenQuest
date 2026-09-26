using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Config;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Districts;

/// <summary>The outcome of checking a drawn outline: the derived polygon and centre, or what is wrong with it.</summary>
public sealed record ShapeCheck(IReadOnlyList<GeoPoint> Ring, Polygon? Polygon, GeoPoint Centroid, IReadOnlyList<GeometryProblemDto> Problems)
{
    public bool Valid => Problems.Count == 0 && Polygon is not null;
}

/// <summary>A district that is being created in the same batch, so imports can be checked against each other.</summary>
public sealed record PendingShape(string Name, Polygon Polygon);

public interface IDistrictShapeChecker
{
    /// <summary>
    /// Checks an outline: it has to be a simple polygon (<see cref="PolygonRules"/>) that does not overlap another active district of the
    /// city. <paramref name="ignoreDistrictId"/> is the district being redrawn.
    /// </summary>
    Task<ShapeCheck> CheckAsync(Guid cityId, Guid? ignoreDistrictId, IReadOnlyList<GeoPoint> points,
        IReadOnlyList<PendingShape>? alsoAgainst, CancellationToken ct);
}

public sealed class DistrictShapeChecker(AppDbContext db, IOptions<GamificationOptions> options) : IDistrictShapeChecker
{
    private static readonly GeometryFactory Wgs84 = NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public async Task<ShapeCheck> CheckAsync(Guid cityId, Guid? ignoreDistrictId, IReadOnlyList<GeoPoint> points,
        IReadOnlyList<PendingShape>? alsoAgainst, CancellationToken ct)
    {
        var ring = PolygonRules.Normalize(points);
        var problems = PolygonRules.Validate(points).Select(ToDto).ToList();
        if (problems.Count > 0) return new ShapeCheck(ring, null, default, problems);

        var polygon = BuildPolygon(ring);
        if (!polygon.IsValid)
            return new ShapeCheck(ring, null, default, [new GeometryProblemDto("invalid_geometry", "The outline is not a valid polygon.")]);

        var others = await db.Districts.AsNoTracking()
            .Where(d => d.CityId == cityId && d.IsActive && d.Id != ignoreDistrictId)
            .Select(d => new { d.Id, d.Name, d.Geom }).ToListAsync(ct);
        foreach (var other in others) AddOverlap(problems, polygon, other.Geom, other.Name, other.Id);
        foreach (var pending in alsoAgainst ?? []) AddOverlap(problems, polygon, pending.Polygon, pending.Name, null);

        return new ShapeCheck(ring, polygon, PolygonRules.Centroid(ring), problems);
    }

    /// <summary>The ring as a polygon, oriented counter-clockwise (the order of the stored points is not changed).</summary>
    public static Polygon BuildPolygon(IReadOnlyList<GeoPoint> ring)
    {
        var oriented = PolygonRules.SignedArea(ring) < 0 ? ring.Reverse().ToList() : ring.ToList();
        var coordinates = oriented.Select(p => new Coordinate(p.Lon, p.Lat)).Append(new Coordinate(oriented[0].Lon, oriented[0].Lat)).ToArray();
        return Wgs84.CreatePolygon(coordinates);
    }

    private void AddOverlap(List<GeometryProblemDto> problems, Polygon polygon, Geometry other, string name, Guid? id)
    {
        if (!polygon.EnvelopeInternal.Intersects(other.EnvelopeInternal) || !polygon.Intersects(other)) return;
        var shared = polygon.Intersection(other).Area;
        if (shared <= 0) return; // a shared border is fine
        var ratio = shared / Math.Min(polygon.Area, other.Area);
        if (ratio > options.Value.OverlapToleranceRatio)
            problems.Add(new GeometryProblemDto("overlaps_district", $"The outline overlaps the district '{name}'.",
                DistrictId: id, DistrictName: name, OverlapRatio: Math.Round(ratio, 4)));
    }

    private static GeometryProblemDto ToDto(PolygonProblem p)
        => new(p.Code, p.Message, p.EdgeA, p.EdgeB, p.At?.Lat, p.At?.Lon);
}
