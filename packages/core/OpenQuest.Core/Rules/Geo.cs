using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Rules;

public static class Geo
{
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>Great-circle distance (haversine) in meters.</summary>
    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        var lat1 = ToRad(a.Lat);
        var lat2 = ToRad(b.Lat);
        var dLat = lat2 - lat1;
        var dLon = ToRad(b.Lon - a.Lon);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>True if <paramref name="reported"/> is within <paramref name="radiusMeters"/> of <paramref name="asset"/>.</summary>
    public static bool IsWithinGeofence(GeoPoint asset, GeoPoint reported, double radiusMeters)
        => DistanceMeters(asset, reported) <= radiusMeters;

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
