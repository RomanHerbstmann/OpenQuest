using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Events;

/// <summary>Something that happened in the game and that other parts of the system may react to.</summary>
public interface IDomainEvent;

/// <summary>A moderator accepted a change to an asset attribute: it has to reach the city's open data.</summary>
public sealed record AttributeChangeAccepted(ApprovedContribution Contribution) : IDomainEvent;

/// <summary>A submission was approved. Extension point for rewards (points, badges).</summary>
public sealed record SubmissionApproved(Guid SubmissionId, Guid UserId, Guid QuestId, int RewardPoints) : IDomainEvent;

public sealed record SubmissionRejected(Guid SubmissionId, Guid UserId, Guid QuestId, string Reason) : IDomainEvent;

/// <summary>
/// Publishes an event as part of the caller's unit of work: it becomes visible to handlers only if the caller's
/// transaction commits (transactional outbox), and is delivered at least once.
/// </summary>
public interface IEventPublisher
{
    void Publish(IDomainEvent domainEvent);
}

/// <summary>
/// Reacts to events of one type. Events arrive in batches; handlers must be idempotent because delivery is
/// at-least-once (a failed batch is retried with backoff).
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(IReadOnlyList<TEvent> events, CancellationToken ct);
}
