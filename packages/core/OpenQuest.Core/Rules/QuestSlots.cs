using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Rules;

public static class QuestSlots
{
    /// <summary>
    /// A new claim may be granted only if <c>slotsTaken &lt; maxCompletions</c>, where
    /// <c>slotsTaken = active claims + pending/approved submissions</c> (maintained under a row lock).
    /// Expired/cancelled claims and rejected submissions release their slot.
    /// </summary>
    public static bool HasFreeSlot(int maxCompletions, int slotsTaken) => slotsTaken < maxCompletions;

    public static int FreeSlots(int maxCompletions, int slotsTaken) => Math.Max(0, maxCompletions - slotsTaken);

    /// <summary>
    /// Quest status after its slot count changed: an active quest becomes <c>full</c> when the limit is reached,
    /// a full one becomes <c>active</c> again when a slot is released. Draft/paused/closed are never touched.
    /// </summary>
    public static QuestStatus StatusAfterSlotChange(QuestStatus current, int maxCompletions, int slotsTaken) => current switch
    {
        QuestStatus.Active when slotsTaken >= maxCompletions => QuestStatus.Full,
        QuestStatus.Full when slotsTaken < maxCompletions => QuestStatus.Active,
        _ => current,
    };
}
