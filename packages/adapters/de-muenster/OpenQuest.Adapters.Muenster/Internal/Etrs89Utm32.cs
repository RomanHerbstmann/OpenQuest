using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace OpenQuest.Adapters.Muenster.Internal;

/// <summary>ETRS89 / UTM zone 32N (EPSG:25832), the source CRS of the city's GIS, to WGS84.</summary>
internal static class Etrs89Utm32
{
    private const string Wkt =
        "PROJCS[\"ETRS89 / UTM zone 32N\"," +
        "GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\"," +
        "SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]]," +
        "TOWGS84[0,0,0,0,0,0,0],AUTHORITY[\"EPSG\",\"6258\"]]," +
        "PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]]," +
        "UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4258\"]]," +
        "PROJECTION[\"Transverse_Mercator\"]," +
        "PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",9]," +
        "PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0]," +
        "UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]]," +
        "AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"25832\"]]";

    private static readonly MathTransform ToWgs84 = Build();

    private static MathTransform Build()
    {
        var source = (CoordinateSystem)new CoordinateSystemFactory().CreateFromWkt(Wkt);
        var target = GeographicCoordinateSystem.WGS84;
        return new CoordinateTransformationFactory().CreateFromCoordinateSystems(source, target).MathTransform;
    }

    /// <summary>Converts (easting, northing) in metres to (lat, lon) in degrees.</summary>
    public static (double Lat, double Lon) Convert(double easting, double northing)
    {
        var (lon, lat) = ToWgs84.Transform(easting, northing);
        return (lat, lon);
    }
}
