using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Gamification;

/// <summary>
/// After a sync that changed assets, the genus statistics of all districts are out of date. They are only marked as stale here
/// (<c>genus_stats_at</c> empty) and recalculated the next time somebody needs them, so a sync of a big city costs nothing until then.
/// Before this, the statistics were only refreshed when older than <c>Gamification:GenusStatsMaxAgeHours</c>.
/// </summary>
public sealed class InvalidateGenusStatsHandler(AppDbContext db) : IEventHandler<AssetSyncCompleted>
{
    public async Task HandleAsync(IReadOnlyList<AssetSyncCompleted> events, CancellationToken ct)
    {
        if (!events.Any(e => e.ChangedAssets)) return;
        await db.Database.ExecuteSqlRawAsync("UPDATE district SET genus_stats_at = NULL WHERE genus_stats_at IS NOT NULL", ct);
    }
}
