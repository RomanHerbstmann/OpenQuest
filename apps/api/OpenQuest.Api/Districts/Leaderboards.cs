using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Districts;

/// <summary>Rankings computed from the points ledger, which stores the district in which every point was earned.</summary>
public interface ILeaderboards
{
    /// <summary>The active districts of a city, best first. Null if the city does not exist.</summary>
    Task<IReadOnlyList<DistrictRankingDto>?> DistrictRankingAsync(Guid cityId, LeaderboardPeriod period, CancellationToken ct);

    /// <summary>The best players of a district. Null if the district does not exist.</summary>
    Task<IReadOnlyList<PlayerRankingDto>?> TopPlayersAsync(Guid districtId, LeaderboardPeriod period, int limit, CancellationToken ct);
}

public sealed class Leaderboards(AppDbContext db, TimeProvider clock) : ILeaderboards
{
    public async Task<IReadOnlyList<DistrictRankingDto>?> DistrictRankingAsync(Guid cityId, LeaderboardPeriod period, CancellationToken ct)
    {
        var city = await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cityId, ct);
        if (city is null) return null;
        var since = LeaderboardWindow.StartOf(period, clock.GetUtcNow(), ResolveZone(city.Timezone));

        var districts = await db.Districts.AsNoTracking().Where(d => d.CityId == cityId && d.IsActive)
            .Select(d => new { d.Id, d.Key, d.Name, d.Color }).ToListAsync(ct);
        var ids = districts.Select(d => d.Id).ToList();

        var ledger = db.PointTransactions.AsNoTracking().Where(p => p.DistrictId != null && ids.Contains(p.DistrictId.Value));
        if (since is { } from) ledger = ledger.Where(p => p.CreatedAt >= from);
        var stats = (await ledger.GroupBy(p => p.DistrictId!.Value)
            .Select(g => new { Id = g.Key, Points = g.Sum(p => p.Amount), Contributors = g.Select(p => p.UserId).Distinct().Count(), Lines = g.Count() })
            .ToListAsync(ct)).ToDictionary(x => x.Id);

        var rows = districts
            .Select(d => stats.TryGetValue(d.Id, out var s)
                ? (d.Id, d.Key, d.Name, d.Color, s.Points, s.Contributors, s.Lines)
                : (d.Id, d.Key, d.Name, d.Color, Points: 0, Contributors: 0, Lines: 0))
            .OrderByDescending(r => r.Points).ThenByDescending(r => r.Contributors).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        // equal points and contributors share a rank
        return rows.Select(r => new DistrictRankingDto(
            1 + rows.Count(o => o.Points > r.Points || (o.Points == r.Points && o.Contributors > r.Contributors)),
            r.Id, r.Key, r.Name, r.Color, r.Points, r.Contributors, r.Lines)).ToList();
    }

    public async Task<IReadOnlyList<PlayerRankingDto>?> TopPlayersAsync(Guid districtId, LeaderboardPeriod period, int limit, CancellationToken ct)
    {
        var district = await db.Districts.AsNoTracking().Include(d => d.City).FirstOrDefaultAsync(d => d.Id == districtId, ct);
        if (district is null) return null;
        var since = LeaderboardWindow.StartOf(period, clock.GetUtcNow(), ResolveZone(district.City.Timezone));

        var ledger = db.PointTransactions.AsNoTracking().Where(p => p.DistrictId == districtId);
        if (since is { } from) ledger = ledger.Where(p => p.CreatedAt >= from);
        var top = await ledger.GroupBy(p => p.UserId).Select(g => new { UserId = g.Key, Points = g.Sum(p => p.Amount) })
            .OrderByDescending(x => x.Points).ThenBy(x => x.UserId).Take(limit).ToListAsync(ct);

        var userIds = top.Select(t => t.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return top.Select(t => new PlayerRankingDto(
            1 + top.Count(o => o.Points > t.Points), t.UserId,
            users.TryGetValue(t.UserId, out var u) ? u.Username : "", u?.DisplayName, t.Points)).ToList();
    }

    /// <summary>The city's zone; UTC if the system does not know it (for example without tzdata in a container).</summary>
    public static TimeZoneInfo ResolveZone(string id)
        => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
}
