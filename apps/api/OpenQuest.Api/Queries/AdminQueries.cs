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

/// <summary>Reports and environment readings written by the importer (read only).</summary>
public interface IImportedFeeds
{
    Task<IReadOnlyList<object>> ReportsAsync(string? status, string? category, int limit, CancellationToken ct);
    Task<IReadOnlyList<object>> ReadingsAsync(string? metric, int days, CancellationToken ct);
}

public sealed class ImportedFeeds(AppDbContext db, TimeProvider clock) : IImportedFeeds
{
    public async Task<IReadOnlyList<object>> ReportsAsync(string? status, string? category, int limit, CancellationToken ct)
    {
        var query = db.AssetReports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(r => r.Category == category);
        var rows = await query.OrderByDescending(r => r.ReportedAt).Take(limit)
            .Select(r => new { Report = r, DataSource = r.DataSource.Key }).ToListAsync(ct);
        // Coordinates are read in memory: ST_X/ST_Y don't exist for geography columns.
        return rows.Select(x => (object)new
        {
            x.Report.Id, dataSource = x.DataSource, x.Report.ExternalId, x.Report.Category, x.Report.Status,
            x.Report.Description, x.Report.StatusNotes, x.Report.Address, x.Report.MediaUrl,
            lat = x.Report.Geom.Y, lon = x.Report.Geom.X, x.Report.AssetId, x.Report.DistanceM,
            x.Report.ReportedAt, x.Report.SourceUpdatedAt,
        }).ToList();
    }

    public async Task<IReadOnlyList<object>> ReadingsAsync(string? metric, int days, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-days);
        var query = db.EnvironmentReadings.AsNoTracking().Where(r => r.MeasuredAt >= since);
        if (!string.IsNullOrWhiteSpace(metric)) query = query.Where(r => r.Metric == metric);
        return (await query.OrderByDescending(r => r.MeasuredAt).ThenBy(r => r.Metric)
            .Select(r => new { dataSource = r.DataSource.Key, r.StationId, r.Metric, r.Value, r.Unit, r.MeasuredAt })
            .ToListAsync(ct)).Cast<object>().ToList();
    }
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
                AssetType = s.Claim.Quest.Asset == null ? null : s.Claim.Quest.Asset.AssetType.Key, TaskType = s.Claim.Quest.TaskType.Key,
                District = db.Districts.Where(d => d.Id == s.Claim.Quest.DistrictId).Select(d => new { d.Id, d.Name, d.CentroidLat, d.CentroidLon, d.Color }).FirstOrDefault(),
                Username = db.Users.Where(u => u.Id == s.Claim.UserId).Select(u => u.Username).First(),
                MediaId = db.Media.Where(m => m.SubmissionId == s.Id).Select(m => (Guid?)m.Id).FirstOrDefault(),
            }).ToListAsync(ct);

        return rows.Select(x => new AdminSubmissionDto(
            x.Sub.Id, x.Sub.Status, x.Sub.SubmittedAt, x.Username, x.Quest.Id, x.Quest.Title, x.TaskType,
            JsonNode.Parse(x.Quest.TaskConfig), x.Asset is null || x.AssetType is null ? null : Mapping.ToDto(x.Asset, x.AssetType),
            x.Sub.Location.Y, x.Sub.Location.X, x.Sub.DistanceM, JsonNode.Parse(x.Sub.Payload), x.MediaId,
            x.Sub.RejectionReason, x.Sub.ReviewedAt,
            x.District is null ? null : new QuestAreaDto(x.District.Id, x.District.Name, x.District.CentroidLat, x.District.CentroidLon, x.District.Color),
            x.Sub.AutoReview is null ? null : JsonNode.Parse(x.Sub.AutoReview), x.Sub.ReviewedBy)).ToList();
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
                Quest = q, Asset = q.Asset, AssetType = q.Asset == null ? null : q.Asset.AssetType.Key, TaskType = q.TaskType.Key,
                DistrictName = db.Districts.Where(d => d.Id == q.DistrictId).Select(d => d.Name).FirstOrDefault(),
                Pending = db.Submissions.Count(s => s.Claim.QuestId == q.Id && s.Status == SubmissionStatus.Pending),
                Approved = db.Submissions.Count(s => s.Claim.QuestId == q.Id && s.Status == SubmissionStatus.Approved),
            }).ToListAsync(ct);
        return rows.Select(r => (object)new
        {
            r.Quest.Id, r.Quest.CampaignId, r.Quest.Title, taskType = r.TaskType, status = r.Quest.Status,
            r.Quest.MaxCompletions, r.Quest.SlotsTaken, r.Quest.RewardPoints, r.Quest.GeofenceRadiusM, r.Quest.ClaimTtlMinutes,
            r.Quest.StartsAt, r.Quest.EndsAt, pendingSubmissions = r.Pending, approvedSubmissions = r.Approved,
            asset = r.Asset is null || r.AssetType is null ? null : Mapping.ToDto(r.Asset, r.AssetType),
            r.Quest.DistrictId, r.DistrictName,
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
