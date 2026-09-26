using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class GeoTests
{
    private static readonly GeoPoint Prinzipalmarkt = new(51.9625, 7.6256);

    [Fact]
    public void Distance_to_self_is_zero()
        => Assert.Equal(0, Geo.DistanceMeters(Prinzipalmarkt, Prinzipalmarkt), 3);

    [Fact]
    public void Distance_muenster_to_dortmund_is_about_55km()
    {
        var dortmund = new GeoPoint(51.5136, 7.4653);
        var km = Geo.DistanceMeters(Prinzipalmarkt, dortmund) / 1000;
        Assert.InRange(km, 50, 60);
    }

    [Fact]
    public void One_thousandth_degree_latitude_is_about_111m()
    {
        var d = Geo.DistanceMeters(new GeoPoint(52.000, 7.6), new GeoPoint(52.001, 7.6));
        Assert.InRange(d, 110, 112);
    }

    [Theory]
    [InlineData(0.0002, true)]   // ~22 m
    [InlineData(0.0005, false)]  // ~56 m
    public void Geofence_respects_radius(double latOffset, bool expected)
    {
        var reported = new GeoPoint(Prinzipalmarkt.Lat + latOffset, Prinzipalmarkt.Lon);
        Assert.Equal(expected, Geo.IsWithinGeofence(Prinzipalmarkt, reported, 30));
    }
}
