using NetTopologySuite.Geometries;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Data;

// Mirrors docs/data-model/erd.md. Columns are snake_case (naming convention), enums are snake_case strings,
// "jsonb" columns are held as JSON text (string) and parsed where needed.

public class DataSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string AdapterKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string License { get; set; } = "";
    public string Attribution { get; set; } = "";
    public string Config { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class RunStatus
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
}

public class SyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DataSourceId { get; set; }
    public DataSource DataSource { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = RunStatus.Running;
    /// <summary>Blob key of the full raw download, exactly as delivered by the source.</summary>
    public string? SnapshotKey { get; set; }
    /// <summary>Fingerprint of the source's field list; an unexpected change fails the run.</summary>
    public string? SchemaHash { get; set; }
    /// <summary>Number of records in the download.</summary>
    public int RecordCount { get; set; }
    public int AssetsCreated { get; set; }
    public int AssetsUpdated { get; set; }
    public int AssetsRemoved { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// One version of an asset as seen by one sync run. Rows exist only where something happened (created, changed
/// according to <see cref="SourceHash"/>, or vanished at the source), so an unchanged daily import adds nothing.
/// </summary>
public class AssetSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public Guid SyncRunId { get; set; }
    public AssetChangeType ChangeType { get; set; }
    /// <summary>Position in this version.</summary>
    public Point Geom { get; set; } = null!;
    /// <summary>Source record in this version (JSON).</summary>
    public string Raw { get; set; } = "{}";
    public string SourceHash { get; set; } = "";
}

public class AssetTypeEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public string AttributeSchema { get; set; } = "{}";
}

public class TaskTypeEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigSchema { get; set; } = "{}";
    public string ResultSchema { get; set; } = "{}";
}

public class AssetTypeTaskType
{
    public Guid AssetTypeId { get; set; }
    public Guid TaskTypeId { get; set; }
}

public class AssetEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetTypeId { get; set; }
    public AssetTypeEntity AssetType { get; set; } = null!;
    public Guid DataSourceId { get; set; }
    public string ExternalId { get; set; } = "";
    /// <summary>WGS84 point (PostGIS geography).</summary>
    public Point Geom { get; set; } = null!;
    /// <summary>Normalized attributes (JSON), validated by <see cref="AssetTypeEntity.AttributeSchema"/>.</summary>
    public string Attributes { get; set; } = "{}";
    /// <summary>Untouched source record (JSON).</summary>
    public string Raw { get; set; } = "{}";
    public string SourceHash { get; set; } = "";
    public AssetStatus Status { get; set; } = AssetStatus.Active;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Stored lower-cased; unique.</summary>
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Player;
    public string? DisplayName { get; set; }
    public string Locale { get; set; } = "de";
    public int TotalPoints { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public class UserRecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class QuestCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreatedBy { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string AssetFilter { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Quest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CampaignId { get; set; }
    public Guid AssetId { get; set; }
    public AssetEntity Asset { get; set; } = null!;
    public Guid TaskTypeId { get; set; }
    public TaskTypeEntity TaskType { get; set; } = null!;
    public Guid CreatedBy { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>JSON validated by the task type's config schema, e.g. {"attribute":"genus"}.</summary>
    public string TaskConfig { get; set; } = "{}";
    public int MaxCompletions { get; set; } = 1;
    /// <summary>Active claims + pending/approved submissions. Only changed while holding the quest row lock.</summary>
    public int SlotsTaken { get; set; }
    public int RewardPoints { get; set; } = 10;
    public int GeofenceRadiusM { get; set; } = 30;
    public int ClaimTtlMinutes { get; set; } = 30;
    public QuestStatus Status { get; set; } = QuestStatus.Active;
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Claim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestId { get; set; }
    public Quest Quest { get; set; } = null!;
    public Guid UserId { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Active;
    public DateTimeOffset ClaimedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClaimId { get; set; }
    public Claim Claim { get; set; } = null!;
    /// <summary>Task result (JSON), validated by the task type's result schema.</summary>
    public string Payload { get; set; } = "{}";
    /// <summary>Player position at submit time.</summary>
    public Point Location { get; set; } = null!;
    public double DistanceM { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Pending;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}

public class Media
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;
    public string StorageKey { get; set; } = "";
    public string MimeType { get; set; } = "image/jpeg";
    public int Width { get; set; }
    public int Height { get; set; }
    public int SizeBytes { get; set; }
    /// <summary>SHA-256 of the stored (re-encoded) image, hex.</summary>
    public string Sha256 { get; set; } = "";
    /// <summary>64-bit perceptual difference hash, 16 hex chars.</summary>
    public string Phash { get; set; } = "";
    public DateTimeOffset? CapturedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A city. Districts belong to one city; the time zone decides where a leaderboard week starts.</summary>
public class City
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>URL-safe short name, unique.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>ISO 3166-1 alpha-2, e.g. "DE".</summary>
    public string? CountryCode { get; set; }
    /// <summary>Where the map starts (for the frontend).</summary>
    public double? CenterLat { get; set; }
    public double? CenterLon { get; set; }
    public int? DefaultZoom { get; set; }
    /// <summary>IANA id, e.g. "Europe/Berlin".</summary>
    public string Timezone { get; set; } = "Europe/Berlin";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A part of a city ("Stadtviertel"), drawn by an admin as an ordered ring of points (<see cref="DistrictPoint"/>).
/// <see cref="Geom"/> and the centroid are derived from the points on every save. Districts of one city do not overlap.
/// </summary>
public class District
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CityId { get; set; }
    public City City { get; set; } = null!;
    /// <summary>URL-safe short name, unique within the city.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Map colour, "#RRGGBB".</summary>
    public string? Color { get; set; }
    /// <summary>Deactivated districts keep their history but take no new points and are hidden from players.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>Cache of the ledger sum of this district (like <see cref="User.TotalPoints"/>).</summary>
    public int TotalPoints { get; set; }
    public Polygon Geom { get; set; } = null!;
    /// <summary>Trees with a known genus inside the district, when the genus statistics were last calculated (see <see cref="DistrictGenusStat"/>).</summary>
    public int KnownGenusTrees { get; set; }
    public DateTimeOffset? GenusStatsAt { get; set; }
    public double CentroidLat { get; set; }
    public double CentroidLon { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>How many trees of a genus stand in a district. Recalculated from the assets; the basis for how rare a card of that genus is there.</summary>
public class DistrictGenusStat
{
    public Guid DistrictId { get; set; }
    public string Genus { get; set; } = "";
    public int TreeCount { get; set; }
}

/// <summary>
/// A collected tree card. One per approved submission (unique), so a redelivered event cannot hand out a second card.
/// The rarity is rolled from the submission's id and the frequency of the genus in the district at that time.
/// </summary>
public class Card
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid AssetId { get; set; }
    /// <summary>The district the tree lies in; null if none (or the district was deleted).</summary>
    public Guid? DistrictId { get; set; }
    /// <summary>Latin genus, e.g. "Tilia".</summary>
    public string Genus { get; set; } = "";
    public Rarity Rarity { get; set; }
    /// <summary>How frequent the genus was in the district (before the boosts).</summary>
    public Frequency Frequency { get; set; }
    /// <summary>Share of the genus among the district's trees with a known genus at that time; null if the tree lies in no district.</summary>
    public double? Share { get; set; }
    /// <summary>JSON array of reason codes: scarce_in_district, new_to_district, new_information, condition_fact.</summary>
    public string Reasons { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One corner of a district outline. <see cref="Position"/> is the order in which the corners are connected; the last connects back to the first.</summary>
public class DistrictPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DistrictId { get; set; }
    public int Position { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
}

/// <summary>
/// One line of the points ledger. Append-only: corrections are new lines (which may be negative), never edits.
/// <see cref="User.TotalPoints"/> is a cache of the sum.
/// </summary>
public class PointTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    /// <summary>The submission that earned the points; unique per reason, so a redelivered event cannot pay twice.</summary>
    public Guid? SubmissionId { get; set; }
    /// <summary>The district the points were earned in, at the time of the award. Null if the tree lies in no district.</summary>
    public Guid? DistrictId { get; set; }
    public int Amount { get; set; }
    public PointReason Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AttributeChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public Submission Submission { get; set; } = null!;
    public Guid AssetId { get; set; }
    public AssetEntity Asset { get; set; } = null!;
    public string AttributeKey { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public ChangeStatus Status { get; set; } = ChangeStatus.Proposed;
    public Guid? ExportRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ExportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DataSourceId { get; set; }
    public DataSource DataSource { get; set; } = null!;
    /// <summary>Null for publications triggered by the system (event-driven), set for manual runs.</summary>
    public Guid? CreatedBy { get; set; }
    /// <summary>csv | geojson</summary>
    public string Format { get; set; } = "geojson";
    public string? StorageKey { get; set; }
    public string Status { get; set; } = RunStatus.Running;
    public int ChangeCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum OutboxStatus { Pending, Processed, Dead }

/// <summary>
/// Transactional outbox: domain events are written in the same transaction as the state change they describe
/// and delivered to handlers afterwards (at least once). A database trigger notifies the processor on insert.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Event type name, resolved via EventTypeRegistry.</summary>
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    /// <summary>Earliest time of the next delivery attempt (backoff after failures).</summary>
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
}
