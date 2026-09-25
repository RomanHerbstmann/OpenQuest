using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Adapters;

public sealed record AssetQuery(AssetType AssetType, BBox? BBox = null, DateTimeOffset? UpdatedSince = null);

/// <summary>Describes one concrete data set of a city that an adapter can read (one DATA_SOURCE row).</summary>
public sealed record DataSourceDescriptor(
    string Key,
    AssetType AssetType,
    string Name,
    string City,
    string SourceUrl,
    string License,
    string Attribution);

/// <summary>Read side of a city's open data platform. Consumers that only read (sync) depend on this alone.</summary>
public interface IAssetSource
{
    IAsyncEnumerable<Asset> FetchAssets(AssetQuery query, CancellationToken ct = default);

    Task<Asset?> GetAsset(AssetType assetType, string externalId, CancellationToken ct = default);
}

/// <summary>
/// Pluggable reader for a city's open data platform. Everything city-specific (URLs, field names, CRS) lives in
/// implementations. Write-back is a separate concern: see <see cref="Publishing.IContributionPublisher"/>.
/// </summary>
public interface IDataSourceAdapter : IAssetSource
{
    /// <summary>Adapter key, e.g. "de-muenster". Selected via configuration.</summary>
    string Id { get; }
    string Name { get; }

    /// <summary>The data sets this adapter can read.</summary>
    IReadOnlyCollection<DataSourceDescriptor> DataSources { get; }
}
