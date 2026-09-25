using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenQuest.Core.Adapters;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Tests;

/// <summary>An API whose data source adapter delivers whatever the test scripted, to test the import in isolation.</summary>
public sealed class SyncFactory : ApiFactory
{
    public ScriptedAdapter Adapter { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IDataSourceAdapter>();
        services.AddSingleton<IDataSourceAdapter>(Adapter);
    }
}

public sealed class ScriptedAdapter : IDataSourceAdapter
{
    public const string SourceKey = "de-muenster-trees";

    /// <summary>The next download, set by the test.</summary>
    public SourceSnapshot Next { get; set; } = null!;

    public string Id => "de-muenster";
    public string Name => "scripted";

    public IReadOnlyCollection<DataSourceDescriptor> DataSources { get; } =
    [
        new(SourceKey, AssetType.Tree, "Scripted trees", "Testcity", "https://example.org", "dl-de/by-2.0", "Attribution for tests"),
    ];

    public Task<SourceSnapshot> FetchSnapshotAsync(AssetQuery query, CancellationToken ct = default) => Task.FromResult(Next);

    public Task<Asset?> GetAsset(AssetType assetType, string externalId, CancellationToken ct = default)
        => Task.FromResult(Next.Assets.FirstOrDefault(a => a.ExternalId == externalId));

    public static Asset Tree(string id, double lat, double lon, string? genus, params string[] flags) => new(
        SourceKey, AssetType.Tree, id, new GeoPoint(lat, lon),
        new Dictionary<string, string?> { ["genus"] = genus, ["genus_raw"] = genus, ["street_key"] = "00001" },
        flags, $$"""{"id":"{{id}}","genus":"{{genus}}"}""");

    public static SourceSnapshot Snapshot(IReadOnlyList<Asset> assets, string[]? fields = null) => new(
        assets, System.Text.Encoding.UTF8.GetBytes("RAW:" + string.Join(",", assets.Select(a => a.ExternalId + "=" + a.Attributes["genus"]))),
        "application/geo+json", "geojson", fields ?? ["baumgruppe", "str_schl"], assets.Count);
}
