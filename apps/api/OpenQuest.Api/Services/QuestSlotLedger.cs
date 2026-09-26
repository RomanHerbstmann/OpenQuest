using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Services;

/// <summary>
/// Slot accounting of quests. <c>quest.slots_taken</c> may only change while the quest row is locked, so
/// concurrent claims, cancellations, expiries and reviews are serialized per quest.
/// </summary>
public interface IQuestSlotLedger
{
    /// <summary>Locks the quest row (<c>SELECT … FOR UPDATE</c>) in the current transaction. False if it does not exist.</summary>
    Task<bool> LockQuestAsync(Guid questId, CancellationToken ct);

    /// <summary>Adjusts the slot counter and the derived quest status. The caller must hold the lock.</summary>
    void ChangeSlots(Quest quest, int delta);

    /// <summary>Expires overdue active claims of the quest and releases their slots. Caller must hold the lock.</summary>
    Task<int> ReleaseStaleClaimsAsync(Quest quest, DateTimeOffset now, CancellationToken ct);
}

public sealed class QuestSlotLedger(AppDbContext db) : IQuestSlotLedger
{
    public async Task<bool> LockQuestAsync(Guid questId, CancellationToken ct)
        => (await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM quest WHERE id = {questId} FOR UPDATE").ToListAsync(ct)).Count > 0;

    public void ChangeSlots(Quest quest, int delta)
    {
        quest.SlotsTaken += delta;
        quest.Status = QuestSlots.StatusAfterSlotChange(quest.Status, quest.MaxCompletions, quest.SlotsTaken);
    }

    public async Task<int> ReleaseStaleClaimsAsync(Quest quest, DateTimeOffset now, CancellationToken ct)
    {
        var stale = await db.Claims.Where(c => c.QuestId == quest.Id && c.Status == ClaimStatus.Active && c.ExpiresAt <= now).ToListAsync(ct);
        foreach (var c in stale)
        {
            c.Status = ClaimStatus.Expired;
            c.ClosedAt = now;
        }
        if (stale.Count > 0) ChangeSlots(quest, -stale.Count);
        return stale.Count;
    }
}
