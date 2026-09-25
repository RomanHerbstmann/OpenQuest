using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Config;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Queries;

/// <summary>Read side for the player map (queries, no state changes).</summary>
public interface INearbyQuests
{
    Task<IReadOnlyList<QuestDto>> FindAsync(double lat, double lon, double? radiusMeters, string? taskType, Guid userId, CancellationToken ct);
}

public interface INearbyAssets
{
    Task<IReadOnlyList<AssetDto>> FindAsync(double lat, double lon, double? radiusMeters, string? assetType, CancellationToken ct);
}

public interface IPlayerClaims
{
    Task<IReadOnlyList<MyClaimDto>> ListAsync(Guid userId, CancellationToken ct);
}

public static class Mapping
{
    public static AssetDto ToDto(AssetEntity a, string assetTypeKey) =>
        new(a.Id, assetTypeKey, a.ExternalId, a.Geom.Y, a.Geom.X, JsonNode.Parse(a.Attributes));

    public static QuestDto ToQuestDto(Quest q, string taskTypeKey, AssetEntity asset, string assetTypeKey, double? distance) =>
        new(q.Id, q.Title, q.Description, taskTypeKey, JsonNode.Parse(q.TaskConfig), q.RewardPoints,
            QuestSlots.FreeSlots(q.MaxCompletions, q.SlotsTaken), q.GeofenceRadiusM, q.EndsAt, distance, ToDto(asset, assetTypeKey));
}

public sealed class NearbyQuests(AppDbContext db, TimeProvider clock, IOptions<GameOptions> game) : INearbyQuests
{
    private static readonly GeometryFactory Wgs84 = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public async Task<IReadOnlyList<QuestDto>> FindAsync(double lat, double lon, double? radiusMeters, string? taskType, Guid me, CancellationToken ct)
    {
        var o = game.Value;
        var meters = Math.Clamp(radiusMeters ?? 500, 1, o.MaxNearbyRadiusMeters);
        var now = clock.GetUtcNow();
        var here = Wgs84.CreatePoint(new Coordinate(lon, lat));

        var rows = await db.Quests.AsNoTracking()
            .Where(q => q.Status == QuestStatus.Active && q.SlotsTaken < q.MaxCompletions
                        && q.Asset.Status == AssetStatus.Active
                        && (q.StartsAt == null || q.StartsAt <= now) && (q.EndsAt == null || q.EndsAt > now)
                        && (taskType == null || q.TaskType.Key == taskType)
                        && q.Asset.Geom.IsWithinDistance(here, meters)
                        && !db.Claims.Any(c => c.QuestId == q.Id && c.UserId == me
                                               && (c.Status == ClaimStatus.Active || c.Status == ClaimStatus.Submitted)))
            .Select(q => new { Quest = q, Asset = q.Asset, AssetType = q.Asset.AssetType.Key, TaskType = q.TaskType.Key, Distance = q.Asset.Geom.Distance(here) })
            .OrderBy(x => x.Distance)
            .Take(o.MaxNearbyResults)
            .ToListAsync(ct);

        return rows.Select(x => Mapping.ToQuestDto(x.Quest, x.TaskType, x.Asset, x.AssetType, Math.Round(x.Distance, 1))).ToList();
    }
}

public sealed class NearbyAssets(AppDbContext db, IOptions<GameOptions> game) : INearbyAssets
{
    private static readonly GeometryFactory Wgs84 = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public async Task<IReadOnlyList<AssetDto>> FindAsync(double lat, double lon, double? radiusMeters, string? assetType, CancellationToken ct)
    {
        var o = game.Value;
        var meters = Math.Clamp(radiusMeters ?? 300, 1, o.MaxNearbyRadiusMeters);
        var here = Wgs84.CreatePoint(new Coordinate(lon, lat));
        var rows = await db.Assets.AsNoTracking()
            .Where(a => a.Status == AssetStatus.Active && (assetType == null || a.AssetType.Key == assetType)
                        && a.Geom.IsWithinDistance(here, meters))
            .OrderBy(a => a.Geom.Distance(here))
            .Take(o.MaxNearbyResults)
            .Select(a => new { Asset = a, Type = a.AssetType.Key })
            .ToListAsync(ct);
        return rows.Select(x => Mapping.ToDto(x.Asset, x.Type)).ToList();
    }
}

public sealed class PlayerClaims(AppDbContext db, TimeProvider clock) : IPlayerClaims
{
    public async Task<IReadOnlyList<MyClaimDto>> ListAsync(Guid me, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Claims.AsNoTracking()
            .Where(c => c.UserId == me)
            .OrderByDescending(c => c.ClaimedAt)
            .Take(100)
            .Select(c => new
            {
                Claim = c, Quest = c.Quest, Asset = c.Quest.Asset,
                AssetType = c.Quest.Asset.AssetType.Key, TaskType = c.Quest.TaskType.Key,
                Sub = db.Submissions.Where(s => s.ClaimId == c.Id)
                    .Select(s => new SubmissionSummaryDto(s.Id, s.Status, s.RejectionReason, s.SubmittedAt)).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(x => new MyClaimDto(
            x.Claim.Id,
            x.Claim.Status == ClaimStatus.Active && x.Claim.ExpiresAt <= now ? ClaimStatus.Expired : x.Claim.Status,
            x.Claim.ClaimedAt, x.Claim.ExpiresAt,
            Mapping.ToQuestDto(x.Quest, x.TaskType, x.Asset, x.AssetType, null), x.Sub)).ToList();
    }
}
