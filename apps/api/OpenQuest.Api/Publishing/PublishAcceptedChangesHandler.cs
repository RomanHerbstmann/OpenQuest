using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Events;
using OpenQuest.Core.Publishing;

namespace OpenQuest.Api.Publishing;

/// <summary>Maps stored attribute changes and new-tree proposals to the export model.</summary>
public static class ApprovedContributionMapper
{
    /// <summary>The attribute name under which an accepted new-tree proposal appears in the published files.</summary>
    public const string NewTreeAttribute = "new_tree";

    /// <summary>
    /// A proposal has no asset yet: it is published as a feature at the reported position with <c>attribute = "new_tree"</c>, an empty
    /// external id and the reported genus, species, note and photo as new value. The proposal needs its <c>Submission</c> loaded.
    /// </summary>
    public static ApprovedContribution From(AssetProposal p, string dataSourceKey, string assetTypeKey)
    {
        var value = new System.Text.Json.Nodes.JsonObject
        {
            ["genus"] = p.Genus, ["species"] = p.Species, ["note"] = p.Note, ["photo_url"] = p.PhotoUrl,
        };
        return new(p.Id, p.SubmissionId, dataSourceKey, assetTypeKey, "", new GeoPoint(p.Geom.Y, p.Geom.X), NewTreeAttribute, null,
            value.ToJsonString(), p.Submission.ReviewedAt ?? p.CreatedAt);
    }

    /// <summary>The change needs its <c>Asset</c> (with <c>AssetType</c>) and <c>Submission</c> loaded.</summary>
    public static ApprovedContribution From(AttributeChange c, string dataSourceKey)
        => new(c.Id, c.SubmissionId, dataSourceKey, c.Asset.AssetType.Key, c.Asset.ExternalId,
            new GeoPoint(c.Asset.Geom.Y, c.Asset.Geom.X), c.AttributeKey, c.OldValue, c.NewValue,
            c.Submission.ReviewedAt ?? c.CreatedAt);
}

/// <summary>
/// The moment a moderator accepts a change, it is pushed to every configured publisher (only when <c>Publishing:Enabled</c>, off by default). This is what keeps the city's
/// open data current: no schedule, no manual export. Idempotent: changes that are already exported are skipped, so a
/// redelivered event does no harm.
/// </summary>
public sealed class PublishAcceptedChangesHandler(
    AppDbContext db,
    IPublishingGate gate,
    IEnumerable<IContributionPublisher> publishers,
    TimeProvider clock,
    ILogger<PublishAcceptedChangesHandler> log) : IEventHandler<AttributeChangeAccepted>
{
    public async Task HandleAsync(IReadOnlyList<AttributeChangeAccepted> events, CancellationToken ct)
    {
        if (!gate.Enabled)
        {
            // Publishing is off (ADR-0014): the changes stay accepted, in our database, and the game keeps using them. Nothing is lost:
            // once it is switched on, POST /admin/publications/retry publishes everything that was accepted meanwhile.
            log.LogDebug("Publishing to open data is off; {Count} accepted change(s) stay in the database.", events.Count);
            return;
        }
        var ids = events.Select(e => e.Contribution.ChangeId).Distinct().ToList();
        var pending = await Query().Where(c => ids.Contains(c.Id) && c.Status == ChangeStatus.Accepted).ToListAsync(ct);
        var pendingProposals = await Proposals().Where(p => ids.Contains(p.Id) && p.Status == ChangeStatus.Accepted).ToListAsync(ct);

        var sourceIds = pending.Select(c => c.Asset.DataSourceId).Concat(pendingProposals.Select(p => p.DataSourceId)).Distinct().ToList();
        foreach (var sourceId in sourceIds)
        {
            var source = await db.DataSources.AsNoTracking().FirstAsync(d => d.Id == sourceId, ct);
            var news = pending.Where(c => c.Asset.DataSourceId == sourceId).ToList();
            var newProposals = pendingProposals.Where(p => p.DataSourceId == sourceId).ToList();
            var treeKey = AssetType.Tree.Key;

            // changes and new trees go into the same file; each publication rewrites it completely
            var exported = await Query().Where(c => c.Status == ChangeStatus.Exported && c.Asset.DataSourceId == source.Id).ToListAsync(ct);
            var exportedProposals = await Proposals().Where(p => p.Status == ChangeStatus.Exported && p.DataSourceId == source.Id).ToListAsync(ct);
            var newContributions = news.Select(c => ApprovedContributionMapper.From(c, source.Key))
                .Concat(newProposals.Select(p => ApprovedContributionMapper.From(p, source.Key, treeKey))).OrderBy(c => c.ApprovedAt).ToList();
            var all = exported.Select(c => ApprovedContributionMapper.From(c, source.Key))
                .Concat(exportedProposals.Select(p => ApprovedContributionMapper.From(p, source.Key, treeKey)))
                .Concat(newContributions).OrderBy(c => c.ApprovedAt).ToList();
            var batch = new PublishBatch(source.Key, source.Attribution, newContributions, all, clock.GetUtcNow());

            var locations = new List<string>();
            foreach (var publisher in publishers)
            {
                var result = await publisher.PublishAsync(batch, ct);   // throws -> the outbox retries the whole batch
                locations.Add(result.Location);
                log.LogInformation("Published {Count} change(s) of {Source} via {Publisher}: {Location}",
                    newContributions.Count, source.Key, publisher.Name, result.Location);
            }

            var run = new ExportRun
            {
                DataSourceId = source.Id, CreatedBy = null, Format = "geojson", Status = RunStatus.Succeeded,
                StorageKey = locations.FirstOrDefault(), ChangeCount = newContributions.Count, CreatedAt = clock.GetUtcNow(),
            };
            db.ExportRuns.Add(run);
            foreach (var c in news)
            {
                c.Status = ChangeStatus.Exported;
                c.ExportRunId = run.Id;
            }
            foreach (var p in newProposals)
            {
                p.Status = ChangeStatus.Exported;
                p.ExportRunId = run.Id;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private IQueryable<AssetProposal> Proposals() => db.AssetProposals.Include(p => p.Submission);

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
        var proposals = await db.AssetProposals.Include(p => p.Submission).Where(p => p.Status == ChangeStatus.Accepted).ToListAsync(ct);
        var sources = await db.DataSources.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Key, ct);
        foreach (var c in accepted)
            events.Publish(new AttributeChangeAccepted(ApprovedContributionMapper.From(c, sources[c.Asset.DataSourceId])));
        foreach (var p in proposals)
            events.Publish(new AttributeChangeAccepted(ApprovedContributionMapper.From(p, sources[p.DataSourceId], AssetType.Tree.Key)));
        await db.SaveChangesAsync(ct);
        return accepted.Count + proposals.Count;
    }
}
