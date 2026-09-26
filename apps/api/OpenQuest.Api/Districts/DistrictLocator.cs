using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;

namespace OpenQuest.Api.Districts;

/// <summary>Finds the district a position or an asset lies in. Districts of a city do not overlap, so the answer is unique.</summary>
public interface IDistrictLocator
{
    Task<Guid?> FindForAssetAsync(Guid assetId, CancellationToken ct);
    Task<Guid?> FindAtAsync(double lat, double lon, CancellationToken ct);
}

public sealed class DistrictLocator(AppDbContext db) : IDistrictLocator
{
    // ORDER BY area: if two cities were ever drawn over each other, the most specific district wins.
    public async Task<Guid?> FindForAssetAsync(Guid assetId, CancellationToken ct)
        => (await db.Database.SqlQuery<Guid>($"""
            SELECT d.id AS "Value" FROM district d JOIN asset a ON ST_Covers(d.geom, a.geom)
            WHERE a.id = {assetId} AND d.is_active ORDER BY ST_Area(d.geom) LIMIT 1
            """).ToListAsync(ct)) is [var id, ..] ? id : null;

    public async Task<Guid?> FindAtAsync(double lat, double lon, CancellationToken ct)
        => (await db.Database.SqlQuery<Guid>($"""
            SELECT d.id AS "Value" FROM district d
            WHERE d.is_active AND ST_Covers(d.geom, ST_SetSRID(ST_MakePoint({lon}, {lat}), 4326)::geography)
            ORDER BY ST_Area(d.geom) LIMIT 1
            """).ToListAsync(ct)) is [var id, ..] ? id : null;
}
