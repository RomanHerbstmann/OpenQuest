using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Gamification;

/// <summary>Read side: a player's points, level and ledger.</summary>
public interface IPlayerProgress
{
    Task<PlayerProgressDto> GetAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<PointTransactionDto>> ListPointsAsync(Guid userId, int offset, int limit, CancellationToken ct);
}

public sealed class PlayerProgress(AppDbContext db, GamificationProfile profile) : IPlayerProgress
{
    public async Task<PlayerProgressDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var total = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TotalPoints).FirstOrDefaultAsync(ct);
        var p = profile.Levels.ProgressFor(total);
        var cards = await db.Cards.AsNoTracking().CountAsync(c => c.UserId == userId, ct);
        var badges = await db.UserBadges.AsNoTracking().CountAsync(b => b.UserId == userId && db.Badges.Any(x => x.Id == b.BadgeId && x.IsActive), ct);
        return new PlayerProgressDto(total, new LevelDto(p.Level, p.Current, p.Required, p.Percent, p.IsMaxLevel), cards, badges);
    }

    public async Task<IReadOnlyList<PointTransactionDto>> ListPointsAsync(Guid userId, int offset, int limit, CancellationToken ct)
        => await db.PointTransactions.AsNoTracking().Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Skip(offset).Take(limit)
            .Select(p => new PointTransactionDto(
                p.Id, p.Amount, p.Reason, p.SubmissionId,
                db.Submissions.Where(s => s.Id == p.SubmissionId).Select(s => s.Claim.Quest.Title).FirstOrDefault(),
                p.CreatedAt))
            .ToListAsync(ct);
}
