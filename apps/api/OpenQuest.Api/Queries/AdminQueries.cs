using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Queries;

public interface IModerationQueue
{
    Task<IReadOnlyList<AdminSubmissionDto>> ListAsync(SubmissionStatus status, int offset, int limit, CancellationToken ct);
}

public interface IQuestOverview
{
    Task<IReadOnlyList<object>> ListQuestsAsync(QuestStatus? status, int offset, int limit, CancellationToken ct);
    Task<IReadOnlyList<object>> ListCampaignsAsync(int limit, CancellationToken ct);
}

/// <summary>The recorded versions of an asset across sync runs (ASSET_SNAPSHOT).</summary>
public interface IAssetHistory
{
    /// <summary>Null if the asset does not exist.</summary>
    Task<IReadOnlyList<object>?> ListAsync(Guid assetId, CancellationToken ct);
}

public interface IPublicationOverview
{
    Task<IReadOnlyList<object>> ListRunsAsync(int limit, CancellationToken ct);
    Task<object> OutboxStatusAsync(CancellationToken ct);
    Task<IReadOnlyList<object>> SyncRunsAsync(int limit, CancellationToken ct);
}

public sealed class ModerationQueue(AppDbContext db) : IModerationQueue
{
    public async Task<IReadOnlyList<AdminSubmissionDto>> ListAsync(SubmissionStatus status, int offset, int limit, CancellationToken ct)
    {
        var rows = await db.Submissions.AsNoTracking().Where(s => s.Status == status)
            .OrderBy(s => s.SubmittedAt).Skip(offset).Take(limit)
            .Select(s => new
            {
                Sub = s, Quest = s.Claim.Quest, Asset = s.Claim.Quest.Asset,
                AssetType = s.Claim.Quest.Asset.AssetType.Key, TaskType = s.Claim.Quest.TaskType.Key,
                Username = db.Users.Where(u => u.Id == s.Claim.UserId).Select(u => u.Username).First(),
                MediaId = db.Media.Where(m => m.SubmissionId == s.Id).Select(m => (Guid?)m.Id).FirstOrDefault(),
            }).ToListAsync(ct);

        return rows.Select(x => new AdminSubmissionDto(
            x.Sub.Id, x.Sub.Status, x.Sub.SubmittedAt, x.Username, x.Quest.Id, x.Quest.Title, x.TaskType,
            JsonNode.Parse(x.Quest.TaskConfig), Mapping.ToDto(x.Asset, x.AssetType),
            x.Sub.Location.Y, x.Sub.Location.X, x.Sub.DistanceM, JsonNode.Parse(x.Sub.Payload), x.MediaId,
            x.Sub.RejectionReason, x.Sub.ReviewedAt)).ToList();
    }
}

public sealed class QuestOverview(AppDbContext db) : IQuestOverview
{
    public async Task<IReadOnlyList<object>> ListQuestsAsync(QuestStatus? status, int offset, int limit, CancellationToken ct)
    {
        var rows = await db.Quests.AsNoTracking()
            .Where(q => status == null || q.Status == status)
            .OrderByDescending(q => q.CreatedAt).Skip(offset).Take(limit)
            .Select(q => new
            {
                Quest = q, Asset = q.Asset, AssetType = q.Asset.AssetType.Key, TaskType = q.TaskType.Key,
                Pending = db.Submissions.Count(s => s.Claim.QuestId == q.Id && s.Status == SubmissionStatus.Pending),
                Approved = db.Submissions.Count(s => s.Claim.QuestId == q.Id && s.Status == SubmissionStatus.Approved),
            }).ToListAsync(ct);
        return rows.Select(r => (object)new
        {
            r.Quest.Id, r.Quest.CampaignId, r.Quest.Title, taskType = r.TaskType, status = r.Quest.Status,
            r.Quest.MaxCompletions, r.Quest.SlotsTaken, r.Quest.RewardPoints, r.Quest.GeofenceRadiusM, r.Quest.ClaimTtlMinutes,
            r.Quest.StartsAt, r.Quest.EndsAt, pendingSubmissions = r.Pending, approvedSubmissions = r.Approved,
            asset = Mapping.ToDto(r.Asset, r.AssetType),
        }).ToList();
    }

    public async Task<IReadOnlyList<object>> ListCampaignsAsync(int limit, CancellationToken ct)
        => (await db.QuestCampaigns.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(limit)
            .Select(c => new { c.Id, c.Title, c.Description, c.CreatedAt, c.CreatedBy, quests = db.Quests.Count(q => q.CampaignId == c.Id) })
            .ToListAsync(ct)).Cast<object>().ToList();
}

public sealed class AssetHistory(AppDbContext db) : IAssetHistory
{
    public async Task<IReadOnlyList<object>?> ListAsync(Guid assetId, CancellationToken ct)
    {
        if (!await db.Assets.AnyAsync(a => a.Id == assetId, ct)) return null;
        var rows = await db.AssetSnapshots.AsNoTracking().Where(s => s.AssetId == assetId)
            .Join(db.SyncRuns, s => s.SyncRunId, r => r.Id, (s, r) => new { s, r.StartedAt })
            .OrderBy(x => x.StartedAt).ToListAsync(ct);
        return rows.Select(x => (object)new
        {
            syncRunId = x.s.SyncRunId, syncedAt = x.StartedAt, changeType = x.s.ChangeType,
            lat = x.s.Geom.Y, lon = x.s.Geom.X, sourceHash = x.s.SourceHash, raw = JsonNode.Parse(x.s.Raw),
        }).ToList();
    }
}

public sealed class PublicationOverview(AppDbContext db) : IPublicationOverview
{
    public async Task<IReadOnlyList<object>> ListRunsAsync(int limit, CancellationToken ct)
        => (await db.ExportRuns.AsNoTracking().OrderByDescending(e => e.CreatedAt).Take(limit)
            .Select(e => new { e.Id, dataSource = e.DataSource.Key, e.Format, e.Status, e.ChangeCount, location = e.StorageKey, e.CreatedBy, e.CreatedAt })
            .ToListAsync(ct)).Cast<object>().ToList();

    public async Task<object> OutboxStatusAsync(CancellationToken ct)
    {
        var counts = await db.OutboxMessages.AsNoTracking().GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(OutboxStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        var failing = await db.OutboxMessages.AsNoTracking().Where(m => m.Status != OutboxStatus.Processed && m.Attempts > 0)
            .OrderByDescending(m => m.OccurredAt).Take(20)
            .Select(m => new { m.Id, m.Type, m.Status, m.Attempts, m.LastError, m.AvailableAt }).ToListAsync(ct);
        return new { pending = Count(OutboxStatus.Pending), processed = Count(OutboxStatus.Processed), dead = Count(OutboxStatus.Dead), failing };
    }

    public async Task<IReadOnlyList<object>> SyncRunsAsync(int limit, CancellationToken ct)
        => (await db.SyncRuns.AsNoTracking().OrderByDescending(s => s.StartedAt).Take(limit)
            .Select(s => new { s.Id, dataSource = s.DataSource.Key, s.StartedAt, s.FinishedAt, s.Status, s.RecordCount, s.SchemaHash, hasSnapshot = s.SnapshotKey != null, s.AssetsCreated, s.AssetsUpdated, s.AssetsRemoved, s.Error })
            .ToListAsync(ct)).Cast<object>().ToList();
}
