using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Data;
using OpenQuest.Api.Services;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Sync;

/// <summary>Counters of one imported batch.</summary>
public sealed record UpsertOutcome(int Created, int Updated);

/// <summary>Writes a batch of source assets into the database (insert, update, or re-match a moved asset).</summary>
public interface IAssetBatchUpserter
{
    Task<UpsertOutcome> UpsertAsync(AppDbContext db, UpsertContext context, IReadOnlyList<Asset> batch, CancellationToken ct);
}

/// <summary>State that spans all batches of one sync run.</summary>
public sealed class UpsertContext(Guid dataSourceId, Guid assetTypeId, DateTimeOffset runStartedAt, bool dataSourceHadAssets)
{
    public Guid DataSourceId { get; } = dataSourceId;
    public Guid AssetTypeId { get; } = assetTypeId;
    public DateTimeOffset RunStartedAt { get; } = runStartedAt;
    public bool DataSourceHadAssets { get; } = dataSourceHadAssets;
    public HashSet<Guid> MatchedThisRun { get; } = [];
}

public sealed class AssetBatchUpserter(TimeProvider clock) : IAssetBatchUpserter
{
    private static readonly GeometryFactory Wgs84 = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
    /// <summary>A "new" asset within this distance of a vanished one is the same asset that moved slightly.</summary>
    private const double RematchMeters = 1.0;

    public async Task<UpsertOutcome> UpsertAsync(AppDbContext db, UpsertContext ctx, IReadOnlyList<Asset> batch, CancellationToken ct)
    {
        int created = 0, updated = 0;
        var ids = batch.Select(a => a.ExternalId).ToList();
        var existing = await db.Assets.Where(a => a.DataSourceId == ctx.DataSourceId && ids.Contains(a.ExternalId))
            .ToDictionaryAsync(a => a.ExternalId, ct);
        var now = clock.GetUtcNow();
        var unchanged = new List<Guid>();

        foreach (var a in batch)
        {
            var location = Wgs84.CreatePoint(new Coordinate(a.Position.Lon, a.Position.Lat));
            var sourceAttrs = AssetAttributes.FromAdapter(a);
            var hash = AssetAttributes.Hash(sourceAttrs, a.Position);

            if (!existing.TryGetValue(a.ExternalId, out var row) && ctx.DataSourceHadAssets)
                row = await FindMovedAsync(db, ctx, location, ct);

            if (row is null)
            {
                db.Assets.Add(new AssetEntity
                {
                    AssetTypeId = ctx.AssetTypeId, DataSourceId = ctx.DataSourceId, ExternalId = a.ExternalId, Geom = location,
                    Attributes = sourceAttrs.ToJsonString(), Raw = a.RawJson, SourceHash = hash,
                    Status = AssetStatus.Active, FirstSeenAt = now, LastSeenAt = now, UpdatedAt = now,
                });
                created++;
                continue;
            }

            ctx.MatchedThisRun.Add(row.Id);
            if (row.SourceHash == hash && row.Status == AssetStatus.Active)
            {
                unchanged.Add(row.Id);
                continue;
            }

            // Source values overwrite; attributes the source does not deliver stay.
            var merged = JsonUtil.ParseObject(row.Attributes) ?? new JsonObject();
            foreach (var (k, v) in sourceAttrs) merged[k] = v?.DeepClone();
            row.Attributes = merged.ToJsonString();
            row.Geom = location;
            row.Raw = a.RawJson;
            row.SourceHash = hash;
            row.Status = AssetStatus.Active;
            row.LastSeenAt = now;
            row.UpdatedAt = now;
            updated++;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        if (unchanged.Count > 0)
            await db.Assets.Where(a => unchanged.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastSeenAt, now), ct);
        return new UpsertOutcome(created, updated);
    }

    /// <summary>
    /// Finds an existing, not yet seen asset within 1 m: the source has no ids, so a tree whose coordinate changed
    /// slightly would otherwise become a "new" tree and orphan its quests and photos.
    /// </summary>
    private static async Task<AssetEntity?> FindMovedAsync(AppDbContext db, UpsertContext ctx, Point location, CancellationToken ct)
    {
        var candidates = await db.Assets
            .Where(a => a.DataSourceId == ctx.DataSourceId && a.LastSeenAt < ctx.RunStartedAt && a.Geom.IsWithinDistance(location, RematchMeters))
            .OrderBy(a => a.Geom.Distance(location))
            .Take(5)
            .ToListAsync(ct);
        return candidates.FirstOrDefault(c => !ctx.MatchedThisRun.Contains(c.Id));
    }
}

internal static class AssetAttributes
{
    /// <summary>Attributes as delivered by the adapter, plus quality flags.</summary>
    public static JsonObject FromAdapter(Asset a)
    {
        var o = new JsonObject();
        foreach (var (k, v) in a.Attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal)) o[k] = v;
        o["quality_flags"] = new JsonArray(a.QualityFlags.OrderBy(f => f, StringComparer.Ordinal).Select(f => (JsonNode?)f).ToArray());
        return o;
    }

    public static string Hash(JsonObject attributes, GeoPoint p)
    {
        var text = $"{attributes.ToJsonString()}|{p.Lat:F7}|{p.Lon:F7}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
