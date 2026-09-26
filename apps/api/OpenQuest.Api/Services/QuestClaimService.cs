using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Services;

public interface IQuestClaimService
{
    Task<ServiceResult<Claim>> ClaimAsync(Guid userId, Guid questId, CancellationToken ct);
    Task<ServiceResult<bool>> CancelAsync(Guid userId, Guid claimId, CancellationToken ct);
}

public sealed class QuestClaimService(AppDbContext db, IQuestSlotLedger ledger, TimeProvider clock) : IQuestClaimService
{
    /// <summary>
    /// Grants a claim if the quest still has a free slot. Race-safe: the quest row is locked inside a transaction,
    /// so concurrent claims are serialized per quest.
    /// </summary>
    public async Task<ServiceResult<Claim>> ClaimAsync(Guid userId, Guid questId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (!await ledger.LockQuestAsync(questId, ct)) return ServiceResult<Claim>.Fail(404, "quest_not_found");
        var quest = await db.Quests.Include(q => q.Asset).FirstAsync(q => q.Id == questId, ct);
        var districtActive = quest.DistrictId is null || await db.Districts.AnyAsync(d => d.Id == quest.DistrictId && d.IsActive, ct);

        // Stale claims still hold a slot until swept; release them now so they neither block nor count.
        await ledger.ReleaseStaleClaimsAsync(quest, now, ct);

        if (quest.Status is not (QuestStatus.Active or QuestStatus.Full) || quest.Asset is { Status: not AssetStatus.Active } || !districtActive)
            return ServiceResult<Claim>.Fail(409, "quest_unavailable");
        if ((quest.StartsAt is { } s && now < s) || (quest.EndsAt is { } e && now >= e))
            return ServiceResult<Claim>.Fail(409, "quest_not_open");

        var mine = await db.Claims.AsNoTracking().FirstOrDefaultAsync(c =>
            c.QuestId == questId && c.UserId == userId
            && (c.Status == ClaimStatus.Active || c.Status == ClaimStatus.Submitted), ct);
        if (mine is not null)
            return ServiceResult<Claim>.Fail(409, "already_claimed", details: new { claimId = mine.Id });

        if (!QuestSlots.HasFreeSlot(quest.MaxCompletions, quest.SlotsTaken))
        {
            await db.SaveChangesAsync(ct); // persist any released stale claims
            await tx.CommitAsync(ct);
            return ServiceResult<Claim>.Fail(409, "no_free_slots");
        }

        var claim = new Claim
        {
            QuestId = questId,
            UserId = userId,
            Status = ClaimStatus.Active,
            ClaimedAt = now,
            ExpiresAt = ClaimPolicy.ExpiresAt(now, TimeSpan.FromMinutes(quest.ClaimTtlMinutes)),
        };
        db.Claims.Add(claim);
        ledger.ChangeSlots(quest, +1);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult<Claim>.Success(claim);
    }

    public async Task<ServiceResult<bool>> CancelAsync(Guid userId, Guid claimId, CancellationToken ct)
    {
        var questId = await db.Claims.Where(c => c.Id == claimId && c.UserId == userId).Select(c => (Guid?)c.QuestId).FirstOrDefaultAsync(ct);
        if (questId is null) return ServiceResult<bool>.Fail(404, "claim_not_found");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ledger.LockQuestAsync(questId.Value, ct);
        var claim = await db.Claims.Include(c => c.Quest).FirstAsync(c => c.Id == claimId, ct);
        if (claim.Status != ClaimStatus.Active) return ServiceResult<bool>.Fail(409, "claim_not_active");
        claim.Status = ClaimStatus.Cancelled;
        claim.ClosedAt = clock.GetUtcNow();
        ledger.ChangeSlots(claim.Quest, -1);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult<bool>.Success(true);
    }
}
