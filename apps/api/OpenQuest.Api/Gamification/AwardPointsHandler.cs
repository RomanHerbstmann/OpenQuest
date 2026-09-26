using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Api.Districts;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Gamification;

/// <summary>
/// Pays the quest's reward into the points ledger when a submission is approved. Who approves (moderator today, an
/// automatic check later) does not matter: the handler only sees the event.
/// Idempotent, because delivery is at-least-once: a submission is paid once (unique index on submission and reason), and
/// the cached totals of the user and of the district only change together with the ledger line, in one transaction.
/// The district is the one the quest's tree lies in (if any); the ledger keeps it even when the district is redrawn later.
/// </summary>
public sealed class AwardPointsHandler(AppDbContext db, IDistrictLocator districts, TimeProvider clock) : IEventHandler<SubmissionApproved>
{
    public async Task HandleAsync(IReadOnlyList<SubmissionApproved> events, CancellationToken ct)
    {
        var awards = events.Where(e => e.RewardPoints > 0).GroupBy(e => e.SubmissionId).Select(g => g.First()).ToList();
        if (awards.Count == 0) return;

        var ids = awards.Select(a => a.SubmissionId).ToList();
        var alreadyPaid = (await db.PointTransactions.AsNoTracking()
                .Where(p => p.Reason == PointReason.QuestApproved && p.SubmissionId != null && ids.Contains(p.SubmissionId.Value))
                .Select(p => p.SubmissionId).ToListAsync(ct))
            .Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        var todo = awards.Where(a => !alreadyPaid.Contains(a.SubmissionId)).ToList();
        if (todo.Count == 0) return;

        var questIds = todo.Select(a => a.QuestId).Distinct().ToList();
        var quests = await db.Quests.AsNoTracking().Where(q => questIds.Contains(q.Id)).Select(q => new { q.Id, q.AssetId, q.DistrictId }).ToDictionaryAsync(q => q.Id, ct);
        var districtOfAsset = new Dictionary<Guid, Guid?>();
        var districtOf = new Dictionary<Guid, Guid?>(); // by submission
        foreach (var a in todo)
        {
            Guid? district = null;
            if (quests.TryGetValue(a.QuestId, out var quest))
            {
                if (quest.DistrictId is not null) district = quest.DistrictId;   // a quest of a district: the points go there
                else if (quest.AssetId is { } assetId && !districtOfAsset.TryGetValue(assetId, out district))
                    districtOfAsset[assetId] = district = await districts.FindForAssetAsync(assetId, ct);
            }
            districtOf[a.SubmissionId] = district;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var a in todo)
            db.PointTransactions.Add(new PointTransaction
            {
                UserId = a.UserId, SubmissionId = a.SubmissionId, DistrictId = districtOf[a.SubmissionId], Amount = a.RewardPoints,
                Reason = PointReason.QuestApproved, CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);

        foreach (var perUser in todo.GroupBy(a => a.UserId))
        {
            var sum = perUser.Sum(a => a.RewardPoints);
            var userId = perUser.Key;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"user\" SET total_points = total_points + {sum} WHERE id = {userId}", ct);
        }
        foreach (var perDistrict in todo.Where(a => districtOf[a.SubmissionId] is not null).GroupBy(a => districtOf[a.SubmissionId]!.Value))
        {
            var sum = perDistrict.Sum(a => a.RewardPoints);
            var districtId = perDistrict.Key;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE district SET total_points = total_points + {sum} WHERE id = {districtId}", ct);
        }
        await tx.CommitAsync(ct);
    }
}
