using System.Text.Json;
using OpenQuest.Adapters.Muenster.Internal;
using OpenQuest.Core.Domain;

namespace OpenQuest.Adapters.Muenster;

/// <summary>
/// Turns the WFS GeoJSON response (EPSG:25832) into normalized core assets. Pure and offline-testable.
/// Fails loudly if the response does not look like what we expect (the WFS may change without notice).
/// </summary>
public static class TreeCatalogParser
{
    public const string AdapterId = "de-muenster";
    public const string DataSourceKey = "de-muenster-trees";
    private const string ExpectedCrs = "25832";
    private const double NearDuplicateMeters = 1.0;

    public static List<Asset> Parse(Stream geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        return Parse(doc.RootElement);
    }

    public static List<Asset> Parse(string geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        return Parse(doc.RootElement);
    }

    private static List<Asset> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("WFS response is not a GeoJSON FeatureCollection.");

        var crs = root.TryGetProperty("crs", out var c) && c.TryGetProperty("properties", out var p)
                  && p.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (crs is null || !crs.Contains(ExpectedCrs, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected CRS '{crs}', expected EPSG:{ExpectedCrs}. Refusing to import.");

        var assets = new List<Asset>(features.GetArrayLength());
        var eastNorth = new List<(double E, double N)>(assets.Capacity);
        var flagLists = new List<List<string>>(assets.Capacity);
        var usedIds = new HashSet<string>();

        foreach (var f in features.EnumerateArray())
        {
            if (!f.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object
                || !props.TryGetProperty("str_schl", out var strEl) || !props.TryGetProperty("baumgruppe", out var genusEl))
                throw new InvalidDataException("Feature is missing expected properties 'str_schl'/'baumgruppe'; source schema changed?");

            if (!f.TryGetProperty("geometry", out var geom) || geom.ValueKind != JsonValueKind.Object
                || geom.GetProperty("type").GetString() != "Point")
                continue; // no usable position, skip

            var coords = geom.GetProperty("coordinates");
            var e = coords[0].GetDouble();
            var nrth = coords[1].GetDouble();

            var rawStreet = strEl.ValueKind == JsonValueKind.String ? strEl.GetString() : null;
            var rawGenus = genusEl.ValueKind == JsonValueKind.String ? genusEl.GetString() : null;

            var (streetKey, padded) = TreeNormalizer.NormalizeStreetKey(rawStreet);
            var genus = TreeNormalizer.NormalizeGenus(rawGenus);

            var flags = new List<string>(genus.Flags);
            if (streetKey is null) flags.Add(TreeNormalizer.FlagStreetKeyMissing);
            else if (padded) flags.Add(TreeNormalizer.FlagStreetKeyPadded);

            var baseId = TreeIds.Create(e, nrth);
            var id = baseId;
            for (var i = 2; !usedIds.Add(id); i++) // same 0.1 m cell twice: keep both, disambiguate deterministically
                id = baseId + "-" + i;

            var (lat, lon) = Etrs89Utm32.Convert(e, nrth);

            var attributes = new Dictionary<string, string?>
            {
                ["genus"] = genus.Genus,
                ["species"] = genus.Species,
                ["genus_raw"] = string.IsNullOrEmpty(rawGenus) ? null : rawGenus,
                ["street_key"] = streetKey,
            };

            assets.Add(new Asset(
                DataSourceKey, AssetType.Tree, id, new GeoPoint(lat, lon),
                attributes, flags, f.GetRawText()));
            eastNorth.Add((e, nrth));
            flagLists.Add(flags);
        }

        FlagNearDuplicates(flagLists, eastNorth);
        return assets;
    }

    /// <summary>Marks trees that have another tree closer than 1 m (grid bucketing, O(n)).</summary>
    private static void FlagNearDuplicates(List<List<string>> flagLists, List<(double E, double N)> eastNorth)
    {
        var grid = new Dictionary<(long, long), List<int>>();
        for (var i = 0; i < eastNorth.Count; i++)
        {
            var key = ((long)Math.Floor(eastNorth[i].E), (long)Math.Floor(eastNorth[i].N));
            if (!grid.TryGetValue(key, out var l)) grid[key] = l = [];
            l.Add(i);
        }

        for (var i = 0; i < eastNorth.Count; i++)
        {
            var (e, n) = eastNorth[i];
            var cx = (long)Math.Floor(e);
            var cy = (long)Math.Floor(n);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (!grid.TryGetValue((cx + dx, cy + dy), out var bucket)) continue;
                foreach (var j in bucket)
                {
                    if (j == i) continue;
                    var dist = Math.Sqrt(Math.Pow(e - eastNorth[j].E, 2) + Math.Pow(n - eastNorth[j].N, 2));
                    if (dist < NearDuplicateMeters)
                    {
                        if (!flagLists[i].Contains(TreeNormalizer.FlagNearDuplicate))
                            flagLists[i].Add(TreeNormalizer.FlagNearDuplicate);
                        goto nextTree;
                    }
                }
            }
            nextTree:;
        }
    }
}
