using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Events;

/// <summary>Something that happened in the game and that other parts of the system may react to.</summary>
public interface IDomainEvent;

/// <summary>A moderator accepted a change to an asset attribute: it has to reach the city's open data.</summary>
public sealed record AttributeChangeAccepted(ApprovedContribution Contribution) : IDomainEvent;

/// <summary>
/// A player handed in a submission (it is pending). Extension point for everything that looks at a fresh submission, such as the
/// automatic check (<see cref="OpenQuest.Core.Review.ISubmissionAutoReviewer"/>).
/// </summary>
public sealed record SubmissionSubmitted(Guid SubmissionId, Guid UserId, Guid QuestId, string TaskType, bool HasPhoto) : IDomainEvent;

/// <summary>A player earned a badge (and its bonus points, if the badge has any). Extension point for notifications.</summary>
public sealed record BadgeAwarded(Guid UserId, Guid BadgeId, string BadgeKey, int RewardPoints) : IDomainEvent;

/// <summary>A submission was approved. Extension point for rewards (points, badges).</summary>
public sealed record SubmissionApproved(Guid SubmissionId, Guid UserId, Guid QuestId, int RewardPoints) : IDomainEvent;

public sealed record SubmissionRejected(Guid SubmissionId, Guid UserId, Guid QuestId, string Reason) : IDomainEvent;

/// <summary>
/// The importer finished a sync run of a data source successfully and committed its changes. Extension point for everything
/// that depends on the asset data (statistics, recurring quests) so that it does not have to poll.
/// </summary>
public sealed record AssetSyncCompleted(
    Guid RunId, Guid DataSourceId, string DataSourceKey, int AssetsCreated, int AssetsUpdated, int AssetsRemoved) : IDomainEvent
{
    public bool ChangedAssets => AssetsCreated + AssetsUpdated + AssetsRemoved > 0;
}

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
