using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Services;
using OpenQuest.Api.Storage;

namespace OpenQuest.Api.Sync;

/// <summary>
/// Admin control over the importer. The API does not import anything itself (ADR-0001): it records the wish in <c>sync_request</c> and
/// sends <c>pg_notify('sync_requested')</c>; the importer's <c>serve</c> command runs the sync and writes the outcome back into the request.
/// </summary>
public interface ISyncRequests
{
    Task<ServiceResult<IReadOnlyList<SyncRequestDto>>> RequestAsync(Guid adminId, SyncRequestBody body, CancellationToken ct);
    Task<IReadOnlyList<SyncRequestDto>> ListAsync(int limit, CancellationToken ct);
}

public sealed partial class SyncRequests(AppDbContext db, TimeProvider clock) : ISyncRequests
{
    public const string Channel = "sync_requested";
    private const int MaxSources = 20;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex KeyPattern();

    public async Task<ServiceResult<IReadOnlyList<SyncRequestDto>>> RequestAsync(Guid adminId, SyncRequestBody body, CancellationToken ct)
    {
        var keys = (body.Sources is { Count: > 0 } ? body.Sources.Select(k => k?.Trim() ?? "").Distinct().ToList() : [SyncRequestKeys.All]);
        if (keys.Count > MaxSources) return Fail(400, "validation_failed", $"At most {MaxSources} sources per request.");
        var bad = keys.Where(k => k != SyncRequestKeys.All && !KeyPattern().IsMatch(k)).ToList();
        if (bad.Count > 0 || (keys.Count > 1 && keys.Contains(SyncRequestKeys.All)))
            return Fail(400, "validation_failed", "sources are data source keys (lowercase letters, digits, '.', '_', '-'); leave the list out to sync all enabled sources.",
                new { invalid = bad });

        var now = clock.GetUtcNow();
        var requests = keys.Select(k => new SyncRequest
        {
            DataSourceKey = k, Force = body.Force == true, AcceptSchemaChange = body.AcceptSchemaChange == true,
            RequestedBy = adminId, RequestedAt = now, Status = SyncRequestStatus.Pending,
        }).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.SyncRequests.AddRange(requests);
            await db.SaveChangesAsync(ct);
            foreach (var r in requests)
            {
                var id = r.Id.ToString();
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify({Channel}, {id})", ct);   // delivered when the transaction commits
            }
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Fail(409, "already_requested", "A sync of this source is already waiting or running.");
        }
        var username = await db.Users.AsNoTracking().Where(u => u.Id == adminId).Select(u => u.Username).FirstOrDefaultAsync(ct);
        return ServiceResult<IReadOnlyList<SyncRequestDto>>.Success(requests.Select(r => ToDto(r, username)).ToList());
    }

    public async Task<IReadOnlyList<SyncRequestDto>> ListAsync(int limit, CancellationToken ct)
    {
        var rows = await db.SyncRequests.AsNoTracking().OrderByDescending(r => r.RequestedAt).Take(limit)
            .Select(r => new { Request = r, Username = db.Users.Where(u => u.Id == r.RequestedBy).Select(u => u.Username).FirstOrDefault() }).ToListAsync(ct);
        return rows.Select(x => ToDto(x.Request, x.Username)).ToList();
    }

    private static SyncRequestDto ToDto(SyncRequest r, string? username) => new(
        r.Id, r.DataSourceKey == SyncRequestKeys.All ? null : r.DataSourceKey, r.Force, r.AcceptSchemaChange, r.Status, username,
        r.RequestedAt, r.StartedAt, r.FinishedAt, r.SyncRunId, r.Error);

    private static ServiceResult<IReadOnlyList<SyncRequestDto>> Fail(int status, string code, string message, object? details = null)
        => ServiceResult<IReadOnlyList<SyncRequestDto>>.Fail(status, code, message, details);
}

/// <summary>The raw download of a sync run, as the importer stored it.</summary>
public interface ISyncSnapshots
{
    Task<ServiceResult<SnapshotDownload>> OpenAsync(Guid runId, CancellationToken ct);
}

/// <summary>A file (<c>Zip</c> false) or an archive with the main download and the reference files of the run (<c>Zip</c> true).</summary>
public sealed record SnapshotDownload(string FileName, string ContentType, Func<Stream, CancellationToken, Task> WriteAsync);

public sealed class SyncSnapshots(AppDbContext db, ISnapshotReader snapshots) : ISyncSnapshots
{
    private const string ManifestSuffix = ".manifest.json";

    public async Task<ServiceResult<SnapshotDownload>> OpenAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.SyncRuns.AsNoTracking().Include(r => r.DataSource).FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return ServiceResult<SnapshotDownload>.Fail(404, "run_not_found");
        if (run.SnapshotKey is null) return ServiceResult<SnapshotDownload>.Fail(404, "no_snapshot", "This run did not get as far as storing a download.");

        var stem = $"{run.DataSource.Key}-{run.StartedAt:yyyyMMdd-HHmmss}";
        var head = await snapshots.GetAsync(run.SnapshotKey, ct);
        if (head is null)
            return ServiceResult<SnapshotDownload>.Fail(404, "snapshot_unavailable",
                "The download is not in the snapshot store. It is only readable when the importer keeps its snapshots in S3 ([snapshots] backend = \"s3\").");

        if (!run.SnapshotKey.EndsWith(ManifestSuffix, StringComparison.Ordinal))
            return ServiceResult<SnapshotDownload>.Success(new SnapshotDownload($"{stem}.{Extension(run.SnapshotKey)}", ContentTypeOf(run.SnapshotKey),
                (stream, token) => stream.WriteAsync(head, token).AsTask()));

        // A run with reference files: the manifest lists them; they go into one archive.
        var manifest = JsonDocument.Parse(head).RootElement;
        var mainKey = manifest.GetProperty("main").GetString()!;
        var extras = manifest.GetProperty("extras").EnumerateObject().Select(e => (Name: e.Name, Key: e.Value.GetString()!)).ToList();
        return ServiceResult<SnapshotDownload>.Success(new SnapshotDownload($"{stem}.zip", "application/zip", async (stream, token) =>
        {
            // ZipArchive writes synchronously, which the response stream does not allow: build the archive in memory, then send it
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                await AddAsync(zip, $"{stem}/main.{Extension(mainKey)}", mainKey, token);
                foreach (var (name, key) in extras) await AddAsync(zip, $"{stem}/extras/{Safe(name)}.{Extension(key)}", key, token);
            }
            buffer.Position = 0;
            await buffer.CopyToAsync(stream, token);
        }));
    }

    private async Task AddAsync(ZipArchive zip, string entryName, string key, CancellationToken ct)
    {
        var bytes = await snapshots.GetAsync(key, ct) ?? throw new InvalidOperationException($"Snapshot file {key} is missing in the store.");
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var s = entry.Open();
        await s.WriteAsync(bytes, ct);
    }

    // extra names look like "enrichment:geo.area_name:2"
    private static string Safe(string name) => Regex.Replace(name, "[^A-Za-z0-9._-]", "_");
    private static string Extension(string key) => key[(key.LastIndexOf('/') + 1)..].Split('.', 2)[1];

    private static string ContentTypeOf(string key) => Extension(key) switch
    {
        "geojson" => "application/geo+json",
        "json" => "application/json",
        "csv" => "text/csv; charset=utf-8",
        "xml" or "gml" => "application/xml",
        "tif" or "tiff" => "image/tiff",
        _ => "application/octet-stream",
    };
}
