using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Publishing;

/// <summary>
/// One batch of accepted changes for a data source.
/// <paramref name="AllChanges"/> is everything published so far including this batch (for rolling "latest" files).
/// </summary>
public sealed record PublishBatch(
    string DataSourceKey,
    string Attribution,
    IReadOnlyList<ApprovedContribution> NewChanges,
    IReadOnlyList<ApprovedContribution> AllChanges,
    DateTimeOffset At);

/// <summary>Where the published data can be found afterwards (URL or storage key).</summary>
public sealed record PublishResult(string Location);

/// <summary>
/// A channel through which accepted changes reach the city's open data (public feed, GitHub repository, ...).
/// Must be idempotent: the same batch may be delivered more than once.
/// </summary>
public interface IContributionPublisher
{
    string Name { get; }

    Task<PublishResult> PublishAsync(PublishBatch batch, CancellationToken ct);
}
