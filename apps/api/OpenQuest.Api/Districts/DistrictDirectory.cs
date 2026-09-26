using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Districts;

/// <summary>Read side for cities and districts (players and the admin panel).</summary>
public interface IDistrictDirectory
{
    Task<IReadOnlyList<CityDto>> ListCitiesAsync(bool includeInactive, CancellationToken ct);
    /// <summary>By id or by key. Null if unknown.</summary>
    Task<CityDto?> GetCityAsync(string idOrKey, CancellationToken ct);
    /// <summary>Null if the city does not exist.</summary>
    Task<IReadOnlyList<DistrictDto>?> ListDistrictsAsync(Guid cityId, bool includeGeometry, bool includeInactive, CancellationToken ct);
    Task<DistrictDto?> GetDistrictAsync(Guid id, bool includeInactive, CancellationToken ct);
    Task<DistrictDto?> LookupAsync(double lat, double lon, CancellationToken ct);
}

public sealed class DistrictDirectory(AppDbContext db, IDistrictLocator locator, ILeaderboards leaderboards) : IDistrictDirectory
{
    public async Task<IReadOnlyList<CityDto>> ListCitiesAsync(bool includeInactive, CancellationToken ct)
    {
        var cities = await db.Cities.AsNoTracking().Where(c => includeInactive || c.IsActive).OrderBy(c => c.Name).ToListAsync(ct);
        var counts = await ActiveDistrictCountsAsync(ct);
        return cities.Select(c => ToDto(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<CityDto?> GetCityAsync(string idOrKey, CancellationToken ct)
    {
        var city = Guid.TryParse(idOrKey, out var id)
            ? await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            : await db.Cities.AsNoTracking().FirstOrDefaultAsync(c => c.Key == idOrKey.ToLower(), ct);
        return city is null ? null : ToDto(city, (await ActiveDistrictCountsAsync(ct)).GetValueOrDefault(city.Id));
    }

    public async Task<IReadOnlyList<DistrictDto>?> ListDistrictsAsync(Guid cityId, bool includeGeometry, bool includeInactive, CancellationToken ct)
    {
        if (!await db.Cities.AnyAsync(c => c.Id == cityId, ct)) return null;
        var districts = await db.Districts.AsNoTracking().Where(d => d.CityId == cityId && (includeInactive || d.IsActive))
            .OrderBy(d => d.Name).ToListAsync(ct);
        var rings = await LoadRingsAsync(districts.Select(d => d.Id).ToList(), ct);
        return districts.Select(d => ToDto(d, rings.GetValueOrDefault(d.Id) ?? [], includeGeometry)).ToList();
    }

    public async Task<DistrictDto?> GetDistrictAsync(Guid id, bool includeInactive, CancellationToken ct)
    {
        var d = await db.Districts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (includeInactive || x.IsActive), ct);
        if (d is null) return null;
        var ring = (await LoadRingsAsync([id], ct)).GetValueOrDefault(id) ?? [];
        var ranking = await leaderboards.DistrictRankingAsync(d.CityId, LeaderboardPeriod.All, ct);
        var mine = ranking?.FirstOrDefault(r => r.DistrictId == id);
        return ToDto(d, ring, includeGeometry: true) with { Rank = mine?.Rank, Contributors = mine?.Contributors ?? 0 };
    }

    public async Task<DistrictDto?> LookupAsync(double lat, double lon, CancellationToken ct)
    {
        var id = await locator.FindAtAsync(lat, lon, ct);
        return id is null ? null : await GetDistrictAsync(id.Value, includeInactive: false, ct);
    }

    private async Task<Dictionary<Guid, int>> ActiveDistrictCountsAsync(CancellationToken ct)
        => await db.Districts.AsNoTracking().Where(d => d.IsActive).GroupBy(d => d.CityId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private async Task<Dictionary<Guid, List<GeoPoint>>> LoadRingsAsync(List<Guid> districtIds, CancellationToken ct)
    {
        var points = await db.DistrictPoints.AsNoTracking().Where(p => districtIds.Contains(p.DistrictId))
            .OrderBy(p => p.DistrictId).ThenBy(p => p.Position).ToListAsync(ct);
        return points.GroupBy(p => p.DistrictId).ToDictionary(g => g.Key, g => g.Select(p => new GeoPoint(p.Lat, p.Lon)).ToList());
    }

    public static CityDto ToDto(City c, int districtCount)
        => new(c.Id, c.Key, c.Name, c.CountryCode, c.CenterLat, c.CenterLon, c.DefaultZoom, c.Timezone, c.IsActive, districtCount);

    public static DistrictDto ToDto(District d, IReadOnlyList<GeoPoint> ring, bool includeGeometry)
        => new(d.Id, d.CityId, d.Key, d.Name, d.Description, d.Color, d.IsActive, d.TotalPoints,
            d.CentroidLat, d.CentroidLon, ring.Count, includeGeometry ? GeoJsonPolygons.Write(ring) : null);
}
