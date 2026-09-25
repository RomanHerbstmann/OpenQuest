using Microsoft.Extensions.Options;
using OpenQuest.Core.Adapters;
using OpenQuest.Core.Domain;

namespace OpenQuest.Adapters.Muenster;

/// <summary>Reads the Baumkataster of Stadt Münster from the city's WFS (see ADR-0001). Read-only.</summary>
public sealed class MuensterAdapter(HttpClient http, IOptions<MuensterOptions> options) : IDataSourceAdapter
{
    public string Id => TreeCatalogParser.AdapterId;
    public string Name => "Stadt Münster (Open Data)";
    public IReadOnlyCollection<DataSourceDescriptor> DataSources { get; } =
    [
        new DataSourceDescriptor(
            Key: TreeCatalogParser.DataSourceKey,
            AssetType: AssetType.Tree,
            Name: "Digitales Baumkataster Münster",
            City: "Münster",
            SourceUrl: "https://opendata.stadt-muenster.de/dataset/digitales-baumkataster-m%C3%BCnster",
            License: "dl-de/by-2.0",
            Attribution:
                "Datenquelle: Stadt Münster, Digitales Baumkataster, dl-de/by-2-0 (https://www.govdata.de/dl-de/by-2-0), " +
                "https://opendata.stadt-muenster.de/dataset/digitales-baumkataster-m%C3%BCnster. " +
                "Daten bereinigt und angereichert durch Team OpenQuest."),
    ];

    public async Task<SourceSnapshot> FetchSnapshotAsync(AssetQuery query, CancellationToken ct = default)
    {
        EnsureSupported(query.AssetType);

        // One request for the whole layer (~7 MB): unordered WFS paging could skip or repeat features.
        var url = $"{options.Value.WfsUrl}?SERVICE=WFS&VERSION=1.1.0&REQUEST=GetFeature&TYPENAME=Baeume" +
                  "&OUTPUTFORMAT=geojson&SRSNAME=EPSG:25832";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsByteArrayAsync(ct);

        var parsed = TreeCatalogParser.ParseCatalog(new MemoryStream(raw));
        var assets = query.BBox is { } b
            ? parsed.Assets.Where(a => a.Position.Lon >= b.MinLon && a.Position.Lon <= b.MaxLon
                                       && a.Position.Lat >= b.MinLat && a.Position.Lat <= b.MaxLat).ToList()
            : parsed.Assets;
        return new SourceSnapshot(assets, raw, "application/geo+json", "geojson", parsed.Fields, parsed.RecordCount);
    }

    public async Task<Asset?> GetAsset(AssetType assetType, string externalId, CancellationToken ct = default)
    {
        // The WFS has no id lookup (ids are derived by us), so this downloads the layer. Prefer the local DB.
        var snapshot = await FetchSnapshotAsync(new AssetQuery(assetType), ct);
        return snapshot.Assets.FirstOrDefault(a => a.ExternalId == externalId);
    }

    private void EnsureSupported(AssetType type)
    {
        if (type.Key != AssetType.Tree.Key)
            throw new NotSupportedException($"Adapter '{Id}' does not support asset type '{type.Key}'.");
    }
}
