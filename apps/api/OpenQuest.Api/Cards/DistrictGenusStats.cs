using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Cards;

/// <param name="Share">Share of the genus among the district's trees with a known genus (0 to 1); 0 if there is none of it.</param>
/// <param name="Sample">Number of trees with a known genus in the district.</param>
public sealed record GenusShare(double Share, int Sample);

/// <summary>
/// How frequent each genus is in a district, counted from the assets that lie inside its outline. The statistics are kept in
/// <c>district_genus_stat</c> and recalculated when they are missing, when the district was redrawn or changed since, or when
/// they are older than <c>Gamification:GenusStatsMaxAgeHours</c> (the importer changes the assets on its own schedule).
/// </summary>
public interface IDistrictGenusStats
{
    Task<GenusShare> GetShareAsync(Guid districtId, string genus, CancellationToken ct);
    /// <summary>Recalculates now. Returns the number of genera counted.</summary>
    Task<int> RefreshAsync(Guid districtId, CancellationToken ct);
    /// <summary>The genera of a district, most frequent first. Null if the district does not exist.</summary>
    Task<GenusStatsDto?> ListAsync(Guid districtId, int limit, bool includeInactive, CancellationToken ct);
}

public sealed class DistrictGenusStats(AppDbContext db, GamificationProfile profile, IOptions<GamificationOptions> options, TimeProvider clock)
    : IDistrictGenusStats
{
    public async Task<GenusShare> GetShareAsync(Guid districtId, string genus, CancellationToken ct)
    {
        var district = await EnsureFreshAsync(districtId, ct);
        if (district is null || district.KnownGenusTrees == 0) return new GenusShare(0, 0);
        var count = await db.DistrictGenusStats.AsNoTracking()
            .Where(s => s.DistrictId == districtId && s.Genus == genus).Select(s => s.TreeCount).FirstOrDefaultAsync(ct);
        return new GenusShare(count / (double)district.KnownGenusTrees, district.KnownGenusTrees);
    }

    public async Task<GenusStatsDto?> ListAsync(Guid districtId, int limit, bool includeInactive, CancellationToken ct)
    {
        var district = await EnsureFreshAsync(districtId, ct);
        if (district is null || (!district.IsActive && !includeInactive)) return null;
        var rows = await db.DistrictGenusStats.AsNoTracking().Where(s => s.DistrictId == districtId)
            .OrderByDescending(s => s.TreeCount).ThenBy(s => s.Genus).ToListAsync(ct);
        var total = Math.Max(1, district.KnownGenusTrees);
        var genera = rows.Take(limit).Select(s =>
        {
            var share = s.TreeCount / (double)total;
            return new GenusStatDto(s.Genus, s.TreeCount, share, OpenQuest.Core.Rules.CardRarity.Classify(share, district.KnownGenusTrees, profile.Rarity));
        }).ToList();
        return new GenusStatsDto(districtId, district.KnownGenusTrees, rows.Count, district.GenusStatsAt, genera);
    }

    public async Task<int> RefreshAsync(Guid districtId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // one recalculation at a time per district; a second caller waits and then finds fresh numbers
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"genus-stats:" + districtId}))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM district_genus_stat WHERE district_id = {districtId}", ct);
        var genera = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO district_genus_stat (district_id, genus, tree_count)
            SELECT d.id, a.attributes->>'genus', count(*)
            FROM district d JOIN asset a ON ST_Covers(d.geom, a.geom)
            WHERE d.id = {districtId} AND a.status = 'active' AND coalesce(a.attributes->>'genus', '') <> ''
            GROUP BY d.id, a.attributes->>'genus'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE district SET genus_stats_at = {now},
                known_genus_trees = (SELECT coalesce(sum(tree_count), 0) FROM district_genus_stat WHERE district_id = {districtId})
            WHERE id = {districtId}
            """, ct);
        await tx.CommitAsync(ct);
        return genera;
    }

    private async Task<District?> EnsureFreshAsync(Guid districtId, CancellationToken ct)
    {
        var district = await db.Districts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == districtId, ct);
        if (district is null) return null;
        var maxAge = TimeSpan.FromHours(Math.Max(1, options.Value.GenusStatsMaxAgeHours));
        var now = clock.GetUtcNow();
        // >= so that a district changed in the same instant as the last calculation is recalculated
        var stale = district.GenusStatsAt is not { } at || district.UpdatedAt >= at || now - at > maxAge;
        if (!stale) return district;
        await RefreshAsync(districtId, ct);
        return await db.Districts.AsNoTracking().FirstAsync(d => d.Id == districtId, ct);
    }
}
