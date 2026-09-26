using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Assets;
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

/// <summary>Quests of the districts a position lies in (task type <c>report_new_tree</c>): there is no asset to be near, the player has to be inside the district.</summary>
public interface IAreaQuests
{
    Task<IReadOnlyList<QuestDto>> FindAsync(double lat, double lon, string? taskType, Guid userId, CancellationToken ct);
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

    public static QuestDto ToQuestDto(Quest q, string taskTypeKey, AssetEntity? asset, string? assetTypeKey, double? distance, QuestAreaDto? area = null) =>
        new(q.Id, q.Title, q.Description, taskTypeKey, JsonNode.Parse(q.TaskConfig), q.RewardPoints,
            QuestSlots.FreeSlots(q.MaxCompletions, q.SlotsTaken), q.GeofenceRadiusM, q.EndsAt, distance,
            asset is null || assetTypeKey is null ? null : ToDto(asset, assetTypeKey), area);
}

public sealed class NearbyQuests(AppDbContext db, TimeProvider clock, IOptions<GameOptions> game, IAssetProvenance provenance) : INearbyQuests
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
                        && q.Asset!.Status == AssetStatus.Active
                        && (q.StartsAt == null || q.StartsAt <= now) && (q.EndsAt == null || q.EndsAt > now)
                        && (taskType == null || q.TaskType.Key == taskType)
                        && q.Asset!.Geom.IsWithinDistance(here, meters)
                        && !db.Claims.Any(c => c.QuestId == q.Id && c.UserId == me
                                               && (c.Status == ClaimStatus.Active || c.Status == ClaimStatus.Submitted)))
            .Select(q => new { Quest = q, Asset = q.Asset!, AssetType = q.Asset!.AssetType.Key, TaskType = q.TaskType.Key, Distance = q.Asset!.Geom.Distance(here) })
            .OrderBy(x => x.Distance)
            .Take(o.MaxNearbyResults)
            .ToListAsync(ct);

        var quests = rows.Select(x => Mapping.ToQuestDto(x.Quest, x.TaskType, x.Asset, x.AssetType, Math.Round(x.Distance, 1))).ToList();
        // tell what the city delivered from what players contributed
        var annotated = (await provenance.AnnotateAsync(quests.Where(q => q.Asset is not null).Select(q => q.Asset!).ToList(), ct)).ToDictionary(a => a.Id);
        return quests.Select(q => q.Asset is { } a && annotated.TryGetValue(a.Id, out var withOrigin) ? q with { Asset = withOrigin } : q).ToList();
    }
}

public sealed class AreaQuests(AppDbContext db, TimeProvider clock, IOptions<GameOptions> game) : IAreaQuests
{
    public async Task<IReadOnlyList<QuestDto>> FindAsync(double lat, double lon, string? taskType, Guid me, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var districtIds = await db.Database.SqlQuery<Guid>($"""
            SELECT d.id AS "Value" FROM district d
            WHERE d.is_active AND ST_Covers(d.geom, ST_SetSRID(ST_MakePoint({lon}, {lat}), 4326)::geography)
            """).ToListAsync(ct);
        if (districtIds.Count == 0) return [];

        var rows = await (from q in db.Quests.AsNoTracking()
                          join d in db.Districts.AsNoTracking() on q.DistrictId equals d.Id
                          where districtIds.Contains(d.Id) && q.Status == QuestStatus.Active && q.SlotsTaken < q.MaxCompletions
                                && (q.StartsAt == null || q.StartsAt <= now) && (q.EndsAt == null || q.EndsAt > now)
                                && (taskType == null || q.TaskType.Key == taskType)
                                && !db.Claims.Any(c => c.QuestId == q.Id && c.UserId == me && (c.Status == ClaimStatus.Active || c.Status == ClaimStatus.Submitted))
                          orderby q.CreatedAt descending
                          select new { Quest = q, TaskType = q.TaskType.Key, d.Id, d.Name, d.CentroidLat, d.CentroidLon, d.Color })
            .Take(game.Value.MaxNearbyResults).ToListAsync(ct);
        return rows.Select(x => Mapping.ToQuestDto(x.Quest, x.TaskType, null, null, 0, new QuestAreaDto(x.Id, x.Name, x.CentroidLat, x.CentroidLon, x.Color))).ToList();
    }
}

public sealed class NearbyAssets(AppDbContext db, IOptions<GameOptions> game, IAssetProvenance provenance) : INearbyAssets
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
        return await provenance.AnnotateAsync(rows.Select(x => Mapping.ToDto(x.Asset, x.Type)).ToList(), ct);
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
                AssetType = c.Quest.Asset == null ? null : c.Quest.Asset.AssetType.Key, TaskType = c.Quest.TaskType.Key,
                District = db.Districts.Where(d => d.Id == c.Quest.DistrictId).Select(d => new { d.Id, d.Name, d.CentroidLat, d.CentroidLon, d.Color }).FirstOrDefault(),
                Sub = db.Submissions.Where(s => s.ClaimId == c.Id)
                    .Select(s => new SubmissionSummaryDto(s.Id, s.Status, s.RejectionReason, s.SubmittedAt)).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(x => new MyClaimDto(
            x.Claim.Id,
            x.Claim.Status == ClaimStatus.Active && x.Claim.ExpiresAt <= now ? ClaimStatus.Expired : x.Claim.Status,
            x.Claim.ClaimedAt, x.Claim.ExpiresAt,
            Mapping.ToQuestDto(x.Quest, x.TaskType, x.Asset, x.AssetType, null,
                x.District is null ? null : new QuestAreaDto(x.District.Id, x.District.Name, x.District.CentroidLat, x.District.CentroidLon, x.District.Color)),
            x.Sub)).ToList();
    }
}
