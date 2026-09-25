namespace OpenQuest.Core.Domain;

/// <summary>WGS84 (EPSG:4326) position.</summary>
public readonly record struct GeoPoint(double Lat, double Lon);

/// <summary>WGS84 bounding box.</summary>
public readonly record struct BBox(double MinLon, double MinLat, double MaxLon, double MaxLat);
