using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;

namespace OpenQuest.Api.Gamification;

/// <summary>
/// Pays the quest's reward into the points ledger when a submission is approved. Who approves (moderator today, an
/// automatic check later) does not matter: the handler only sees the event.
/// Idempotent, because delivery is at-least-once: a submission is paid once (unique index on submission and reason), and
/// the user's cached total only changes together with the ledger line, in one transaction.
/// </summary>
public sealed class AwardPointsHandler(AppDbContext db, TimeProvider clock) : IEventHandler<SubmissionApproved>
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

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var a in todo)
            db.PointTransactions.Add(new PointTransaction
            {
                UserId = a.UserId, SubmissionId = a.SubmissionId, Amount = a.RewardPoints,
                Reason = PointReason.QuestApproved, CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);

        foreach (var perUser in todo.GroupBy(a => a.UserId))
        {
            var sum = perUser.Sum(a => a.RewardPoints);
            var userId = perUser.Key;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"user\" SET total_points = total_points + {sum} WHERE id = {userId}", ct);
        }
        await tx.CommitAsync(ct);
    }
}
