namespace OpenQuest.Core.Domain;

/// <summary>WGS84 (EPSG:4326) position.</summary>
public readonly record struct GeoPoint(double Lat, double Lon);
