using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OpenQuest.Adapters.Muenster.Internal;

internal static class TreeIds
{
    /// <summary>
    /// Deterministic id: SHA-256 over the coordinate in the source CRS (EPSG:25832) rounded to 0.1 m.
    /// The source has no stable id. The genus is deliberately not part of the hash, so a corrected genus
    /// does not create a "new" tree; trees that moved slightly are re-matched by proximity during sync.
    /// </summary>
    public static string Create(double easting, double northing)
    {
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{Math.Round(easting, 1):F1}|{Math.Round(northing, 1):F1}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 10).ToLowerInvariant(); // 20 hex chars
    }
}
