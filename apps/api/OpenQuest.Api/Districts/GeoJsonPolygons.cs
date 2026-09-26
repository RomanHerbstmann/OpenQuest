using System.Text.Json.Nodes;
using OpenQuest.Api.Contracts;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Districts;

/// <summary>Reads and writes the outline of a district as a GeoJSON Polygon ([lon, lat], the order of the points is kept).</summary>
public static class GeoJsonPolygons
{
    /// <summary>Accepts a Polygon or a Feature holding one. A closing point equal to the first one is dropped.</summary>
    public static bool TryRead(JsonNode? node, out IReadOnlyList<GeoPoint> ring, out GeometryProblemDto? problem)
    {
        ring = [];
        problem = null;
        if (node is not JsonObject obj)
        {
            problem = new GeometryProblemDto("geometry_required", "A GeoJSON Polygon is required.");
            return false;
        }
        if (obj["type"]?.GetValue<string>() == "Feature")
        {
            if (obj["geometry"] is not JsonObject inner)
            {
                problem = new GeometryProblemDto("geometry_required", "The Feature has no geometry.");
                return false;
            }
            obj = inner;
        }

        var type = obj["type"] is JsonValue t && t.TryGetValue<string>(out var s) ? s : null;
        if (type == "MultiPolygon")
        {
            problem = new GeometryProblemDto("unsupported_geometry", "MultiPolygon is not supported: use one district per polygon.");
            return false;
        }
        if (type != "Polygon")
        {
            problem = new GeometryProblemDto("unsupported_geometry", "Only GeoJSON Polygon is supported.");
            return false;
        }
        if (obj["coordinates"] is not JsonArray rings || rings.Count == 0 || rings[0] is not JsonArray outer)
        {
            problem = new GeometryProblemDto("invalid_geometry", "The Polygon has no coordinates.");
            return false;
        }
        if (rings.Count > 1)
        {
            problem = new GeometryProblemDto("holes_not_supported", "Polygons with holes are not supported.");
            return false;
        }

        var points = new List<GeoPoint>(outer.Count);
        for (var i = 0; i < outer.Count; i++)
        {
            if (outer[i] is JsonArray c && c.Count >= 2 && c[0] is JsonValue x && c[1] is JsonValue y
                && x.TryGetValue<double>(out var lon) && y.TryGetValue<double>(out var lat))
                points.Add(new GeoPoint(lat, lon));
            else
            {
                problem = new GeometryProblemDto("invalid_geometry", $"Coordinate {i} is not a [lon, lat] pair.", EdgeA: i);
                return false;
            }
        }
        ring = points;
        return true;
    }

    /// <summary>A closed GeoJSON Polygon for the points in their order.</summary>
    public static JsonObject Write(IReadOnlyList<GeoPoint> ring)
    {
        var coordinates = new JsonArray();
        foreach (var p in ring) coordinates.Add(new JsonArray(p.Lon, p.Lat));
        if (ring.Count > 0) coordinates.Add(new JsonArray(ring[0].Lon, ring[0].Lat));
        return new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(coordinates) };
    }
}
