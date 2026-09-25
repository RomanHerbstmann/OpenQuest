using System.Security.Cryptography;
using System.Text;
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

/// <summary>
/// One complete download of a data set: the normalized assets, the untouched file as delivered by the source
/// (kept so that every snapshot can be reloaded exactly as it was) and the fields the source delivered.
/// </summary>
public sealed record SourceSnapshot(
    IReadOnlyList<Asset> Assets,
    byte[] RawContent,
    string RawContentType,
    string RawFileExtension,
    IReadOnlyCollection<string> SourceFields,
    int RecordCount)
{
    /// <summary>Fingerprint of the field list. A change means the source's schema changed and the import must not go on silently.</summary>
    public string SchemaHash => Hash(SourceFields);

    public static string Hash(IEnumerable<string> fields)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', fields.Order(StringComparer.Ordinal))))).ToLowerInvariant();
}

/// <summary>Read side of a city's open data platform. Consumers that only read (sync) depend on this alone.</summary>
public interface IAssetSource
{
    /// <summary>Downloads the whole data set. Fails loudly if the response is not what the adapter expects.</summary>
    Task<SourceSnapshot> FetchSnapshotAsync(AssetQuery query, CancellationToken ct = default);

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
