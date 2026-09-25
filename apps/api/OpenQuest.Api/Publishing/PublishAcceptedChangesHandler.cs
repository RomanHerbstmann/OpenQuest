using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Publishing;

namespace OpenQuest.Api.Publishing;

/// <summary>Maps stored attribute changes to the export model.</summary>
public static class ApprovedContributionMapper
{
    /// <summary>The change needs its <c>Asset</c> (with <c>AssetType</c>) and <c>Submission</c> loaded.</summary>
    public static ApprovedContribution From(AttributeChange c, string dataSourceKey)
        => new(c.Id, c.SubmissionId, dataSourceKey, c.Asset.AssetType.Key, c.Asset.ExternalId,
            new GeoPoint(c.Asset.Geom.Y, c.Asset.Geom.X), c.AttributeKey, c.OldValue, c.NewValue,
            c.Submission.ReviewedAt ?? c.CreatedAt);
}

/// <summary>
/// The moment a moderator accepts a change, it is pushed to every configured publisher. This is what keeps the city's
/// open data current: no schedule, no manual export. Idempotent: changes that are already exported are skipped, so a
/// redelivered event does no harm.
/// </summary>
public sealed class PublishAcceptedChangesHandler(
    AppDbContext db,
    IEnumerable<IContributionPublisher> publishers,
    TimeProvider clock,
    ILogger<PublishAcceptedChangesHandler> log) : IEventHandler<AttributeChangeAccepted>
{
    public async Task HandleAsync(IReadOnlyList<AttributeChangeAccepted> events, CancellationToken ct)
    {
        var ids = events.Select(e => e.Contribution.ChangeId).Distinct().ToList();
        var pending = await Query().Where(c => ids.Contains(c.Id) && c.Status == ChangeStatus.Accepted).ToListAsync(ct);

        foreach (var perSource in pending.GroupBy(c => c.Asset.DataSourceId))
        {
            var source = await db.DataSources.AsNoTracking().FirstAsync(d => d.Id == perSource.Key, ct);
            var news = perSource.OrderBy(c => c.Submission.ReviewedAt).ToList();

            var exported = await Query().Where(c => c.Status == ChangeStatus.Exported && c.Asset.DataSourceId == source.Id).ToListAsync(ct);
            var all = exported.Concat(news).OrderBy(c => c.Submission.ReviewedAt)
                .Select(c => ApprovedContributionMapper.From(c, source.Key)).ToList();
            var batch = new PublishBatch(source.Key, source.Attribution,
                news.Select(c => ApprovedContributionMapper.From(c, source.Key)).ToList(), all, clock.GetUtcNow());

            var locations = new List<string>();
            foreach (var publisher in publishers)
            {
                var result = await publisher.PublishAsync(batch, ct);   // throws -> the outbox retries the whole batch
                locations.Add(result.Location);
                log.LogInformation("Published {Count} change(s) of {Source} via {Publisher}: {Location}",
                    news.Count, source.Key, publisher.Name, result.Location);
            }

            var run = new ExportRun
            {
                DataSourceId = source.Id, CreatedBy = null, Format = "geojson", Status = RunStatus.Succeeded,
                StorageKey = locations.FirstOrDefault(), ChangeCount = news.Count, CreatedAt = clock.GetUtcNow(),
            };
            db.ExportRuns.Add(run);
            foreach (var c in news)
            {
                c.Status = ChangeStatus.Exported;
                c.ExportRunId = run.Id;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private IQueryable<AttributeChange> Query() => db.AttributeChanges
        .Include(c => c.Asset).ThenInclude(a => a.AssetType)
        .Include(c => c.Submission);
}

/// <summary>The current published file of a data source (what the city fetches).</summary>
public interface IPublishedFeed
{
    Task<byte[]?> LatestAsync(string dataSourceKey, string extension, CancellationToken ct);
}

public sealed class BlobPublishedFeed(Storage.IBlobReader blobs) : IPublishedFeed
{
    public Task<byte[]?> LatestAsync(string dataSourceKey, string extension, CancellationToken ct)
        => blobs.GetAsync(PublishedKeys.Latest(dataSourceKey, extension), ct);
}

/// <summary>Recovery path: re-emits events for changes that were accepted but never published (for example dead messages).</summary>
public interface IChangeRepublisher
{
    Task<int> RequeueAsync(CancellationToken ct);
}

public sealed class ChangeRepublisher(AppDbContext db, IEventPublisher events) : IChangeRepublisher
{
    public async Task<int> RequeueAsync(CancellationToken ct)
    {
        var accepted = await db.AttributeChanges
            .Include(c => c.Asset).ThenInclude(a => a.AssetType)
            .Include(c => c.Submission)
            .Where(c => c.Status == ChangeStatus.Accepted).ToListAsync(ct);
        var sources = await db.DataSources.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Key, ct);
        foreach (var c in accepted)
            events.Publish(new AttributeChangeAccepted(ApprovedContributionMapper.From(c, sources[c.Asset.DataSourceId])));
        await db.SaveChangesAsync(ct);
        return accepted.Count;
    }
}
