namespace OpenQuest.Core.Domain;

/// <summary>
/// One accepted change to an asset attribute (e.g. genus "Baum Amt62" -> "Tilia"), derived from an approved
/// submission and ready to be published to the city. Values are JSON text.
/// </summary>
public sealed record ApprovedContribution(
    Guid ChangeId,
    Guid SubmissionId,
    string DataSourceKey,
    string AssetTypeKey,
    string ExternalId,
    GeoPoint AssetPosition,
    string AttributeKey,
    string? OldValueJson,
    string? NewValueJson,
    DateTimeOffset ApprovedAt);

public sealed record ExportResult(string ContentType, string FileName, byte[] Content, int Count);
