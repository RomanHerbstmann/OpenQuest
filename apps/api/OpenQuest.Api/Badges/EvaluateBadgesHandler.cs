using OpenQuest.Core.Events;

namespace OpenQuest.Api.Badges;

/// <summary>
/// Looks at a player after an approval and awards the badges they reached. Registered after the handlers that pay points and hand out cards,
/// which the badges count. Idempotent: an evaluation only awards what is missing.
/// </summary>
public sealed class EvaluateBadgesHandler(IBadgeService badges) : IEventHandler<SubmissionApproved>
{
    public async Task HandleAsync(IReadOnlyList<SubmissionApproved> events, CancellationToken ct)
    {
        foreach (var userId in events.Select(e => e.UserId).Distinct())
            await badges.EvaluateUserAsync(userId, ct);
    }
}
