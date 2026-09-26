namespace OpenQuest.Core.Domain;

/// <summary>Why a line was added to the points ledger.</summary>
public enum PointReason
{
    /// <summary>A moderator approved a submission: the quest's reward.</summary>
    QuestApproved,
    /// <summary>A manual adjustment (may be negative).</summary>
    Correction,
    /// <summary>The bonus that comes with a badge (once per badge and player).</summary>
    BadgeReward,
}
