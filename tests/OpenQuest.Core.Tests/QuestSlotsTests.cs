using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class QuestSlotsTests
{
    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, false)]
    [InlineData(1, 1, false)]
    [InlineData(1, 0, true)]
    public void HasFreeSlot_follows_limit_rule(int max, int taken, bool expected)
        => Assert.Equal(expected, QuestSlots.HasFreeSlot(max, taken));

    [Fact]
    public void FreeSlots_never_negative()
        => Assert.Equal(0, QuestSlots.FreeSlots(2, 5));

    [Theory]
    [InlineData(QuestStatus.Active, 3, 3, QuestStatus.Full)]
    [InlineData(QuestStatus.Active, 3, 2, QuestStatus.Active)]
    [InlineData(QuestStatus.Full, 3, 2, QuestStatus.Active)]
    [InlineData(QuestStatus.Full, 3, 3, QuestStatus.Full)]
    [InlineData(QuestStatus.Paused, 3, 0, QuestStatus.Paused)]
    [InlineData(QuestStatus.Closed, 3, 3, QuestStatus.Closed)]
    [InlineData(QuestStatus.Draft, 1, 1, QuestStatus.Draft)]
    public void Status_follows_slot_changes(QuestStatus current, int max, int taken, QuestStatus expected)
        => Assert.Equal(expected, QuestSlots.StatusAfterSlotChange(current, max, taken));
}
