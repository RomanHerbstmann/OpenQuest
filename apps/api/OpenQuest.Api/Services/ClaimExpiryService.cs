using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Services;

/// <summary>Releases the slots of claims that were not completed in time.</summary>
public interface IClaimExpiryService
{
    /// <summary>Returns the number of claims expired.</summary>
    Task<int> ExpireStaleClaimsAsync(CancellationToken ct);
}

public sealed class ClaimExpiryService(AppDbContext db, IQuestSlotLedger ledger, TimeProvider clock) : IClaimExpiryService
{
    public async Task<int> ExpireStaleClaimsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var questIds = await db.Claims.AsNoTracking()
            .Where(c => c.Status == ClaimStatus.Active && c.ExpiresAt <= now)
            .Select(c => c.QuestId).Distinct().OrderBy(id => id).Take(500).ToListAsync(ct);

        var total = 0;
        foreach (var questId in questIds)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await ledger.LockQuestAsync(questId, ct);
            var quest = await db.Quests.FirstAsync(q => q.Id == questId, ct);
            total += await ledger.ReleaseStaleClaimsAsync(quest, now, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
        return total;
    }
}
