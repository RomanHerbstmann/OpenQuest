namespace OpenQuest.Core.Domain;

/// <summary>
/// Normalized real-world object coming from a city's open data.
/// Produced by a data source adapter; nothing city-specific may leak past this type.
/// </summary>
public sealed record Asset(
    string DataSourceKey,
    AssetType AssetType,
    string ExternalId,
    GeoPoint Position,
    IReadOnlyDictionary<string, string?> Attributes,
    IReadOnlyCollection<string> QualityFlags,
    string RawJson);
