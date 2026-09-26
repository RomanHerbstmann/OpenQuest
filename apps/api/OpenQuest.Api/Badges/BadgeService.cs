using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Badges;

/// <summary>Awards badges. The rules are in the core (<see cref="BadgeRules"/>); this reads what a player has done and writes what they earned.</summary>
public interface IBadgeService
{
    /// <summary>What the player has done so far.</summary>
    Task<UserStats> StatsAsync(Guid userId, CancellationToken ct);
    /// <summary>Awards every active badge the player has reached and does not have yet. Returns the keys of the new ones.</summary>
    Task<IReadOnlyList<string>> EvaluateUserAsync(Guid userId, CancellationToken ct);
    /// <summary>Does that for every player (after a badge was added or its criteria changed). Returns how many badges were awarded.</summary>
    Task<int> EvaluateAllAsync(CancellationToken ct);
}

public sealed class BadgeService(AppDbContext db, IEventPublisher events, TimeProvider clock) : IBadgeService
{
    /// <summary>A badge's bonus points can lift a player over the threshold of another badge, so the evaluation repeats until nothing changes (at most this often).</summary>
    private const int MaxRounds = 4;

    public async Task<UserStats> StatsAsync(Guid userId, CancellationToken ct)
    {
        var points = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TotalPoints).FirstOrDefaultAsync(ct);
        var byTask = await db.Submissions.AsNoTracking().Where(s => s.Status == SubmissionStatus.Approved && s.Claim.UserId == userId)
            .GroupBy(s => s.Claim.Quest.TaskType.Key).Select(g => new { Key = g.Key, N = g.Count() }).ToListAsync(ct);
        var byRarity = await db.Cards.AsNoTracking().Where(c => c.UserId == userId)
            .GroupBy(c => c.Rarity).Select(g => new { Rarity = g.Key, N = g.Count() }).ToListAsync(ct);
        var genera = await db.Cards.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.Genus).Distinct().CountAsync(ct);
        var newTrees = await db.AssetProposals.AsNoTracking()
            .CountAsync(p => (p.Status == ChangeStatus.Accepted || p.Status == ChangeStatus.Exported) && p.Submission.Claim.UserId == userId, ct);
        return new UserStats(
            byTask.Sum(x => x.N), points, byRarity.Sum(x => x.N), genera,
            byRarity.ToDictionary(x => x.Rarity, x => x.N), newTrees, byTask.ToDictionary(x => x.Key, x => x.N));
    }

    public async Task<IReadOnlyList<string>> EvaluateUserAsync(Guid userId, CancellationToken ct)
    {
        var awarded = new List<string>();
        var badges = await db.Badges.AsNoTracking().Where(b => b.IsActive).ToListAsync(ct);
        for (var round = 0; round < MaxRounds; round++)
        {
            var earned = (await db.UserBadges.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.BadgeId).ToListAsync(ct)).ToHashSet();
            var open = badges.Where(b => !earned.Contains(b.Id)).ToList();
            if (open.Count == 0) break;
            var stats = await StatsAsync(userId, ct);
            var reached = open.Where(b => BadgeRules.FromJson(b.Criteria) is { } c && BadgeRules.IsMet(c, stats)).ToList();
            if (reached.Count == 0) break;

            var newlyAwarded = await AwardAsync(userId, reached, ct);
            awarded.AddRange(newlyAwarded.Select(b => b.Key));
            if (!newlyAwarded.Any(b => b.RewardPoints > 0)) break;   // no new points, so nothing else can have been reached
        }
        return awarded;
    }

    public async Task<int> EvaluateAllAsync(CancellationToken ct)
    {
        var userIds = await db.Users.AsNoTracking().Select(u => u.Id).ToListAsync(ct);
        var total = 0;
        foreach (var id in userIds) total += (await EvaluateUserAsync(id, ct)).Count;
        return total;
    }

    /// <summary>
    /// Inserts the awards; the primary key of <c>user_badge</c> lets each one through once, also when two evaluations of the same player run at the same time.
    /// The bonus points and the event belong to the award that got in and are written in the same transaction.
    /// </summary>
    private async Task<List<Badge>> AwardAsync(Guid userId, List<Badge> reached, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var won = new List<Badge>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var badge in reached)
        {
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO user_badge (user_id, badge_id, awarded_at) VALUES ({userId}, {badge.Id}, {now}) ON CONFLICT DO NOTHING
                """, ct);
            if (inserted == 0) continue;
            won.Add(badge);
            if (badge.RewardPoints > 0)
                db.PointTransactions.Add(new PointTransaction { UserId = userId, Amount = badge.RewardPoints, Reason = PointReason.BadgeReward, CreatedAt = now });
            events.Publish(new BadgeAwarded(userId, badge.Id, badge.Key, badge.RewardPoints));
        }
        var bonus = won.Sum(b => b.RewardPoints);
        await db.SaveChangesAsync(ct);
        if (bonus > 0) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"user\" SET total_points = total_points + {bonus} WHERE id = {userId}", ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return won;
    }
}
