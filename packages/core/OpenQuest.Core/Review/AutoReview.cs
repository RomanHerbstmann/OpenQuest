using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Review;

/// <summary>What the automatic check decides about a submission.</summary>
public enum AutoReviewVerdict
{
    /// <summary>The photo shows what the quest asked for: the submission may be approved without a moderator.</summary>
    Approve,
    /// <summary>Not sure: a moderator has to look at it.</summary>
    Review,
    /// <summary>Looks wrong (no tree, wrong place, old photo). The submission stays pending with the reasons attached; only a moderator rejects.</summary>
    Reject,
}

/// <summary>What an automatic check gets to see. Positions are WGS84.</summary>
/// <param name="Player">Where the player was when submitting.</param>
/// <param name="Expected">Where the tree should be (the quest's asset); null for a tree that is not in the data yet.</param>
/// <param name="ExpectedGenus">The genus the data has for the tree, if any.</param>
/// <param name="CapturedAt">When the photo was taken, if known.</param>
public sealed record AutoReviewRequest(
    byte[] Image, string ContentType, GeoPoint? Player, double? PlayerAccuracyM, GeoPoint? Expected, string? ExpectedGenus, DateTimeOffset? CapturedAt);

/// <param name="Reasons">Machine readable codes of what was found, for the moderator.</param>
/// <param name="DetailsJson">The full answer of the checker as JSON, kept with the submission.</param>
public sealed record AutoReviewDecision(AutoReviewVerdict Verdict, IReadOnlyList<string> Reasons, string? DetailsJson = null);

/// <summary>
/// Port for an automatic check of a photo submission (phase 6). Implementations must not throw for "cannot decide": return
/// <see cref="AutoReviewVerdict.Review"/> instead. They throw for transient failures (network, overload), which the caller retries.
/// </summary>
public interface ISubmissionAutoReviewer
{
    Task<AutoReviewDecision> ReviewAsync(AutoReviewRequest request, CancellationToken ct);
}

/// <summary>The rule that turns a decision into an action. Only a clear approval acts; nothing is ever rejected automatically.</summary>
public static class AutoReviewPolicy
{
    public static bool ShouldApprove(AutoReviewDecision decision) => decision.Verdict == AutoReviewVerdict.Approve;
}
