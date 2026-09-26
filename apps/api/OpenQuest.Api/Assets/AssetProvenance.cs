using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Config;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Assets;

/// <summary>
/// Tells apart what the city delivered (open data) from what players contributed (ADR-0014). The asset keeps only the city's values; accepted player changes
/// (<c>attribute_change</c>) and reported trees (<c>asset_proposal</c>) are a separate layer that is read here.
/// </summary>
public interface IAssetProvenance
{
    /// <summary>The latest accepted contribution per attribute, for each asset that has any.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<UserContributionDto>>> ContributionsAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct);
    /// <summary>Adds the data source and the contributions to assets that were mapped without them.</summary>
    Task<IReadOnlyList<AssetDto>> AnnotateAsync(IReadOnlyList<AssetDto> assets, CancellationToken ct);
    Task<AssetDetailDto?> DetailAsync(Guid assetId, CancellationToken ct);
    Task<IReadOnlyList<ReportedTreeDto>> ReportedTreesAsync(double lat, double lon, double? radiusMeters, CancellationToken ct);
}

public sealed class AssetProvenance(AppDbContext db, IOptions<GameOptions> game) : IAssetProvenance
{
    private static readonly GeometryFactory Wgs84 = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<UserContributionDto>>> ContributionsAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct)
    {
        if (assetIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<UserContributionDto>>();
        var ids = assetIds.ToList();
        var rows = await (from c in db.AttributeChanges.AsNoTracking()
                          where ids.Contains(c.AssetId) && (c.Status == ChangeStatus.Accepted || c.Status == ChangeStatus.Exported)
                          join s in db.Submissions.AsNoTracking() on c.SubmissionId equals s.Id
                          join cl in db.Claims.AsNoTracking() on s.ClaimId equals cl.Id
                          join u in db.Users.AsNoTracking() on cl.UserId equals u.Id
                          select new { c.AssetId, c.AttributeKey, c.OldValue, c.NewValue, AcceptedAt = s.ReviewedAt ?? c.CreatedAt, SubmissionId = s.Id, u.Username })
            .ToListAsync(ct);
        if (rows.Count == 0) return new Dictionary<Guid, IReadOnlyList<UserContributionDto>>();

        var current = (await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.Attributes }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => JsonNode.Parse(a.Attributes) as JsonObject);
        return rows.GroupBy(r => r.AssetId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<UserContributionDto>)g.GroupBy(r => r.AttributeKey)
                .Select(byKey => byKey.OrderByDescending(r => r.AcceptedAt).ThenByDescending(r => r.SubmissionId).First())
                .OrderBy(r => r.AttributeKey)
                .Select(r =>
                {
                    var value = Parse(r.NewValue);
                    var resolved = AttributeProvenance.Resolve(current.GetValueOrDefault(g.Key)?[r.AttributeKey], true, value, Parse(r.OldValue));
                    return new UserContributionDto(r.AttributeKey, value, Parse(r.OldValue), r.AcceptedAt, r.SubmissionId, r.Username, resolved.ContributionOutdated);
                }).ToList());
    }

    public async Task<IReadOnlyList<AssetDto>> AnnotateAsync(IReadOnlyList<AssetDto> assets, CancellationToken ct)
    {
        if (assets.Count == 0) return assets;
        var ids = assets.Select(a => a.Id).Distinct().ToList();
        var sources = (await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.DataSourceId }).ToListAsync(ct)).ToDictionary(a => a.Id, a => a.DataSourceId);
        var keys = await db.DataSources.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Key, ct);
        var contributions = await ContributionsAsync(ids, ct);
        return assets.Select(a => a with
        {
            DataSource = sources.TryGetValue(a.Id, out var sourceId) ? keys.GetValueOrDefault(sourceId) : null,
            Contributions = contributions.GetValueOrDefault(a.Id) ?? [],
        }).ToList();
    }

    public async Task<AssetDetailDto?> DetailAsync(Guid assetId, CancellationToken ct)
    {
        var row = await db.Assets.AsNoTracking().Where(a => a.Id == assetId)
            .Select(a => new { Asset = a, Type = a.AssetType.Key }).FirstOrDefaultAsync(ct);
        if (row is null) return null;
        var source = await db.DataSources.AsNoTracking().FirstAsync(d => d.Id == row.Asset.DataSourceId, ct);
        var contributions = (await ContributionsAsync([assetId], ct)).GetValueOrDefault(assetId) ?? [];

        var openData = JsonNode.Parse(row.Asset.Attributes) as JsonObject ?? [];
        var byKey = contributions.ToDictionary(c => c.Attribute);
        var effective = openData.Select(kv => kv.Key).Concat(byKey.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).Select(key =>
        {
            byKey.TryGetValue(key, out var c);
            var resolved = AttributeProvenance.Resolve(openData[key], c is not null, c?.Value, c?.PreviousValue);
            var fromUser = resolved.Origin == DataOrigin.User;
            return new EffectiveAttributeDto(key, resolved.Value?.DeepClone(), resolved.Origin, fromUser ? c!.AcceptedAt : null, fromUser ? c!.Username : null);
        }).ToList();

        return new AssetDetailDto(
            row.Asset.Id, row.Type, row.Asset.ExternalId, row.Asset.Geom.Y, row.Asset.Geom.X, openData,
            new DataSourceInfoDto(source.Key, source.Name, source.License, source.Attribution, source.SourceUrl), contributions, effective);
    }

    public async Task<IReadOnlyList<ReportedTreeDto>> ReportedTreesAsync(double lat, double lon, double? radiusMeters, CancellationToken ct)
    {
        var meters = Math.Clamp(radiusMeters ?? 500, 1, game.Value.MaxNearbyRadiusMeters);
        var here = Wgs84.CreatePoint(new Coordinate(lon, lat));
        var rows = await (from p in db.AssetProposals.AsNoTracking()
                          where (p.Status == ChangeStatus.Accepted || p.Status == ChangeStatus.Exported) && p.Geom.IsWithinDistance(here, meters)
                          join s in db.Submissions.AsNoTracking() on p.SubmissionId equals s.Id
                          join cl in db.Claims.AsNoTracking() on s.ClaimId equals cl.Id
                          join u in db.Users.AsNoTracking() on cl.UserId equals u.Id
                          join d in db.DataSources.AsNoTracking() on p.DataSourceId equals d.Id
                          orderby p.Geom.Distance(here)
                          select new { Proposal = p, AcceptedAt = s.ReviewedAt ?? p.CreatedAt, u.Username, Source = d.Key })
            .Take(game.Value.MaxNearbyResults).ToListAsync(ct);
        return rows.Select(r => new ReportedTreeDto(
            r.Proposal.Id, DataOrigin.User, r.Proposal.Geom.Y, r.Proposal.Geom.X, r.Proposal.Genus, r.Proposal.Species, r.Proposal.Note, r.Proposal.PhotoUrl,
            r.Proposal.DistrictId, r.Source, r.Username, r.AcceptedAt)).ToList();
    }

    private static JsonNode? Parse(string? json) => json is null ? null : JsonNode.Parse(json);
}
