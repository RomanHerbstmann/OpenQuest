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

public record Credentials(string? Username, string? Password);
public record RecoverRequest(string? Username, string? RecoveryCode, string? NewPassword);
public record PasswordRequest(string? Password);
public record AuthResponse(string Token, DateTimeOffset ExpiresAt, string Username, string Role);
public record RegisterResponse(string Token, DateTimeOffset ExpiresAt, string Username, string Role, IReadOnlyList<string> RecoveryCodes);
public record RecoverResponse(string Token, DateTimeOffset ExpiresAt, string Username, string Role, int RemainingRecoveryCodes);
public record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
