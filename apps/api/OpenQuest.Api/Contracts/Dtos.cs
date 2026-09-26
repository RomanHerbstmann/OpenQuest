using System.Text.Json.Nodes;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Contracts;

public record AssetDto(Guid Id, string AssetType, string ExternalId, double Lat, double Lon, JsonNode? Attributes);

public record QuestDto(
    Guid Id, string Title, string? Description, string TaskType, JsonNode? TaskConfig,
    int RewardPoints, int FreeSlots, int GeofenceRadiusM, DateTimeOffset? EndsAt, double? DistanceMeters, AssetDto Asset);

public record ClaimDto(Guid Id, Guid QuestId, ClaimStatus Status, DateTimeOffset ClaimedAt, DateTimeOffset ExpiresAt);

public record SubmissionSummaryDto(Guid Id, SubmissionStatus Status, string? RejectionReason, DateTimeOffset SubmittedAt);

public record MyClaimDto(
    Guid Id, ClaimStatus Status, DateTimeOffset ClaimedAt, DateTimeOffset ExpiresAt,
    QuestDto Quest, SubmissionSummaryDto? Submission);

public record BBoxDto(double MinLon, double MinLat, double MaxLon, double MaxLat);

/// <summary>Selects the assets to create quests for. At least one selector is required.</summary>
public record QuestTarget(
    string? AssetType,
    List<Guid>? AssetIds,
    BBoxDto? BBox,
    /// <summary>JSONB containment against asset attributes, e.g. {"genus":null} or {"quality_flags":["placeholder_genus"]}.</summary>
    JsonObject? AttributeFilter,
    bool? WithoutApprovedPhoto,
    int? Limit);

public record CreateQuestsRequest(
    string TaskType,
    string? Title,
    string? Description,
    JsonObject? TaskConfig,
    int MaxCompletions,
    int RewardPoints,
    int? GeofenceRadiusM,
    int? ClaimTtlMinutes,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    QuestStatus? Status,
    QuestTarget Target);

public record CreateQuestsResult(int Created, Guid? CampaignId, IReadOnlyList<Guid>? QuestIds);

public record SetQuestStatusRequest(QuestStatus Status);

public record ReviewRequest(bool Approved, string? Reason);

public record AdminSubmissionDto(
    Guid Id, SubmissionStatus Status, DateTimeOffset SubmittedAt, string Username,
    Guid QuestId, string QuestTitle, string TaskType, JsonNode? TaskConfig,
    AssetDto Asset, double ReportedLat, double ReportedLon, double DistanceMeters,
    JsonNode? Payload, Guid? MediaId, string? RejectionReason, DateTimeOffset? ReviewedAt);

// ---- cities and districts ------------------------------------------------------------------------------------------

public record CityRequest(string? Key, string? Name, string? CountryCode, double? CenterLat, double? CenterLon, int? DefaultZoom, string? Timezone, bool? IsActive);

public record CityDto(
    Guid Id, string Key, string Name, string? CountryCode, double? CenterLat, double? CenterLon, int? DefaultZoom,
    string Timezone, bool IsActive, int DistrictCount);

/// <summary><c>Geometry</c> is a GeoJSON Polygon (or a Feature holding one): coordinates are [lon, lat], the ring may be closed or open, the order of the points is kept.</summary>
public record DistrictRequest(string? Key, string? Name, string? Description, string? Color, bool? IsActive, JsonNode? Geometry);

/// <summary>Body of PUT .../geometry and of the dry run: a GeoJSON Polygon; <c>DistrictId</c> excludes that district from the overlap check when redrawing it.</summary>
public record GeometryRequest(JsonNode? Geometry, Guid? DistrictId);

public record DistrictImportRequest(JsonNode? GeoJson, string? NameProperty, string? KeyProperty, string? DescriptionProperty);

/// <summary><c>Geometry</c> is a GeoJSON Polygon with the points in their drawn order (only when asked for). Rank and contributors are only filled on the detail endpoint.</summary>
public record DistrictDto(
    Guid Id, Guid CityId, string Key, string Name, string? Description, string? Color, bool IsActive, int TotalPoints,
    double CentroidLat, double CentroidLon, int PointCount, JsonNode? Geometry, int? Rank = null, int? Contributors = null);

/// <summary>One problem with a drawn shape. Edge indices count the points of the ring (0 = edge from the first to the second point).</summary>
public record GeometryProblemDto(
    string Code, string Message, int? EdgeA = null, int? EdgeB = null, double? Lat = null, double? Lon = null,
    Guid? DistrictId = null, string? DistrictName = null, double? OverlapRatio = null);

public record GeometryCheckDto(bool Valid, int PointCount, double? CentroidLat, double? CentroidLon, IReadOnlyList<GeometryProblemDto> Problems);

public record ImportResultDto(int Created, IReadOnlyList<DistrictDto> Districts);

public record DistrictRankingDto(int Rank, Guid DistrictId, string Key, string Name, string? Color, int Points, int Contributors, int Contributions);

public record PlayerRankingDto(int Rank, Guid UserId, string Username, string? DisplayName, int Points);

// ---- cards ----------------------------------------------------------------------------------------------------------

public record CardDto(
    Guid Id, string Genus, Rarity Rarity, Frequency Frequency, double? Share, IReadOnlyList<string> Reasons,
    Guid? DistrictId, string? DistrictName, Guid AssetId, double Lat, double Lon, DateTimeOffset ObtainedAt);

public record CollectionEntryDto(string Genus, int Count, Rarity BestRarity, DateTimeOffset FirstObtainedAt);

/// <summary>The player's tree book: every genus collected so far with the best rarity, and totals. <c>ByRarity</c> has all four rarities.</summary>
public record CollectionDto(int TotalCards, int DistinctGenera, IReadOnlyDictionary<string, int> ByRarity, IReadOnlyList<CollectionEntryDto> Genera);

public record GenusStatDto(string Genus, int TreeCount, double Share, Frequency Frequency);

public record GenusStatsDto(Guid DistrictId, int KnownGenusTrees, int GenusCount, DateTimeOffset? CalculatedAt, IReadOnlyList<GenusStatDto> Genera);

public record LevelDto(int Level, int Current, int Required, int Percent, bool IsMaxLevel);
public record PlayerProgressDto(int TotalPoints, LevelDto Level, int CardCount);
public record PointTransactionDto(Guid Id, int Amount, PointReason Reason, Guid? SubmissionId, string? QuestTitle, DateTimeOffset CreatedAt);

public record Credentials(string? Username, string? Password);
public record RecoverRequest(string? Username, string? RecoveryCode, string? NewPassword);
public record PasswordRequest(string? Password);
public record AuthResponse(string Token, DateTimeOffset ExpiresAt, string Username, string Role);
public record RegisterResponse(string Token, DateTimeOffset ExpiresAt, string Username, string Role, IReadOnlyList<string> RecoveryCodes);
public record RecoverResponse(string Token, DateTimeOffset ExpiresAt, string Username, string Role, int RemainingRecoveryCodes);
public record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
