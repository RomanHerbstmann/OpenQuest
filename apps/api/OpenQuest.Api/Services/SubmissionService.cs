using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Data;
using OpenQuest.Api.Photos;
using OpenQuest.Api.Storage;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Services;

public record SubmitInput(double Lat, double Lon, JsonNode? Payload, byte[]? Photo);
public record SubmitResult(Guid SubmissionId, SubmissionStatus Status, double DistanceMeters, Guid? MediaId);

public interface ISubmissionService
{
    Task<ServiceResult<SubmitResult>> SubmitAsync(Guid userId, Guid claimId, SubmitInput input, CancellationToken ct);
}

public sealed class SubmissionService(
    AppDbContext db,
    IJsonSchemaValidator schemas,
    IPhotoIngestor photos,
    IBlobDeleter blobs,
    IAttributeChangeFactory changes,
    TimeProvider clock,
    ILogger<SubmissionService> log) : ISubmissionService
{
    private static readonly GeometryFactory Wgs84 = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public async Task<ServiceResult<SubmitResult>> SubmitAsync(Guid userId, Guid claimId, SubmitInput input, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        if (input.Lat is < -90 or > 90 || input.Lon is < -180 or > 180 || double.IsNaN(input.Lat) || double.IsNaN(input.Lon))
            return Fail("invalid_position");

        var claim = await db.Claims.Include(c => c.Quest).ThenInclude(q => q.Asset)
            .Include(c => c.Quest).ThenInclude(q => q.TaskType)
            .FirstOrDefaultAsync(c => c.Id == claimId && c.UserId == userId, ct);
        if (claim is null) return Fail("claim_not_found", 404);
        if (claim.Status == ClaimStatus.Submitted) return Fail("already_submitted", 409);
        if (claim.Status != ClaimStatus.Active) return Fail("claim_not_active", 409);
        if (ClaimPolicy.IsExpired(claim.ExpiresAt, now)) return Fail("claim_expired", 409); // the expiry job releases the slot

        var quest = claim.Quest;
        TaskTypes.TryParse(quest.TaskType.Key, out var taskType);

        // Payload against the task type's JSON Schema; a photo is required for photo tasks.
        var problems = schemas.Validate(quest.TaskType.ResultSchema, input.Payload ?? new JsonObject());
        if (taskType == TaskType.Photo && input.Photo is not { Length: > 0 }) problems.Add("A photo is required.");
        if (problems.Count > 0) return Fail("invalid_submission", 422, "The submitted values are not valid.", new { problems });

        var assetPos = new GeoPoint(quest.Asset.Geom.Y, quest.Asset.Geom.X);
        var distance = Geo.DistanceMeters(assetPos, new GeoPoint(input.Lat, input.Lon));
        if (distance > quest.GeofenceRadiusM)
            return Fail("outside_geofence", 422, $"You must be within {quest.GeofenceRadiusM} m of the object.",
                new { distanceMeters = Math.Round(distance, 1), allowedMeters = quest.GeofenceRadiusM });

        var submissionId = Guid.NewGuid();
        Media? media = null;
        if (input.Photo is { Length: > 0 })
        {
            var ingested = await photos.IngestAsync(input.Photo, submissionId, now, ct);
            if (!ingested.Ok) return Fail(ingested.Error!.Code, ingested.Error.Status, ingested.Error.Message, ingested.Error.Details);
            media = ingested.Value;
        }

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var status = await db.Database
                .SqlQuery<string>($"SELECT status AS \"Value\" FROM claim WHERE id = {claimId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
            if (status != "active")
            {
                await DeleteQuietly(media?.StorageKey);
                return Fail("claim_not_active", 409);
            }

            db.Submissions.Add(new Submission
            {
                Id = submissionId,
                ClaimId = claimId,
                Payload = (input.Payload ?? new JsonObject()).ToJsonString(),
                Location = Wgs84.CreatePoint(new Coordinate(input.Lon, input.Lat)),
                DistanceM = Math.Round(distance, 1),
                Status = SubmissionStatus.Pending,
                SubmittedAt = now,
            });
            if (media is not null) db.Media.Add(media);

            var change = changes.Propose(quest, taskType, input.Payload as JsonObject, media, submissionId);
            if (change is not null) db.AttributeChanges.Add(change);

            claim.Status = ClaimStatus.Submitted; // slot stays taken: the pending submission now holds it
            claim.ClosedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await DeleteQuietly(media?.StorageKey);
            throw;
        }

        return ServiceResult<SubmitResult>.Success(new SubmitResult(submissionId, SubmissionStatus.Pending, Math.Round(distance, 1), media?.Id));

        static ServiceResult<SubmitResult> Fail(string code, int status = 400, string? message = null, object? details = null)
            => ServiceResult<SubmitResult>.Fail(status, code, message, details);
    }

    private async Task DeleteQuietly(string? key)
    {
        if (key is null) return;
        try { await blobs.DeleteAsync(key); }
        catch (Exception e) { log.LogWarning(e, "Could not delete orphaned photo {Key}", key); }
    }
}
