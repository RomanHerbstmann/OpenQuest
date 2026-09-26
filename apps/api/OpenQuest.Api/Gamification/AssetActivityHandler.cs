using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Gamification;

/// <summary>
/// Keeps <c>asset_activity</c> (when was an asset last checked by a player, how often) up to date when a submission is approved.
/// It recalculates the affected assets from the approved submissions instead of counting up, so a repeated delivery of the same
/// event (at-least-once) changes nothing.
/// </summary>
public sealed class AssetActivityHandler(AppDbContext db) : IEventHandler<SubmissionApproved>
{
    public async Task HandleAsync(IReadOnlyList<SubmissionApproved> events, CancellationToken ct)
    {
        var questIds = events.Select(e => e.QuestId).Distinct().ToList();
        var assetIds = await db.Quests.AsNoTracking().Where(q => questIds.Contains(q.Id)).Where(q => q.AssetId != null).Select(q => q.AssetId!.Value).Distinct().ToArrayAsync(ct);   // a quest without asset verifies nothing
        if (assetIds.Length == 0) return;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO asset_activity (asset_id, last_verified_at, verification_count)
            SELECT q.asset_id, max(s.reviewed_at), count(*)
            FROM submission s
            JOIN claim c ON c.id = s.claim_id
            JOIN quest q ON q.id = c.quest_id
            WHERE q.asset_id = ANY({assetIds}) AND s.status = 'approved' AND s.reviewed_at IS NOT NULL
            GROUP BY q.asset_id
            ON CONFLICT (asset_id) DO UPDATE
                SET last_verified_at = EXCLUDED.last_verified_at, verification_count = EXCLUDED.verification_count
            """, ct);
    }
}
