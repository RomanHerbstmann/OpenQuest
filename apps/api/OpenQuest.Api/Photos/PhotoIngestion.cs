using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Api.Services;
using OpenQuest.Api.Storage;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Photos;

/// <summary>Answers whether (nearly) the same photo was already submitted.</summary>
public interface IPhotoDuplicateFinder
{
    Task<bool> ExistsAsync(long perceptualHash, CancellationToken ct);
}

/// <summary>Compares against all photos of non-rejected submissions (fine at hackathon scale; index it if this grows).</summary>
public sealed class DbPhotoDuplicateFinder(AppDbContext db) : IPhotoDuplicateFinder
{
    private const int MaxDistance = 4;

    public async Task<bool> ExistsAsync(long perceptualHash, CancellationToken ct)
    {
        var hashes = await db.Media.AsNoTracking()
            .Where(m => m.Submission.Status != SubmissionStatus.Rejected)
            .Select(m => m.Phash).ToListAsync(ct);
        return hashes.Any(h => PerceptualHash.HammingDistance(Convert.ToInt64(h, 16), perceptualHash) <= MaxDistance);
    }
}

/// <summary>Validates, cleans, de-duplicates and stores an uploaded photo. Returns the (unsaved) media row.</summary>
public interface IPhotoIngestor
{
    Task<ServiceResult<Media>> IngestAsync(byte[] upload, Guid submissionId, DateTimeOffset now, CancellationToken ct);
}

public sealed class PhotoIngestor(IPhotoProcessor processor, IPhotoDuplicateFinder duplicates, IBlobWriter blobs) : IPhotoIngestor
{
    public async Task<ServiceResult<Media>> IngestAsync(byte[] upload, Guid submissionId, DateTimeOffset now, CancellationToken ct)
    {
        ProcessedPhoto processed;
        try { processed = processor.Process(upload); }
        catch (InvalidImageException e) { return ServiceResult<Media>.Fail(422, "invalid_photo", e.Message); }

        if (await duplicates.ExistsAsync(processed.Hash, ct))
            return ServiceResult<Media>.Fail(409, "duplicate_photo", "This photo was already submitted.");

        var media = new Media
        {
            SubmissionId = submissionId,
            StorageKey = $"submissions/{submissionId}.jpg",
            MimeType = "image/jpeg",
            Width = processed.Width,
            Height = processed.Height,
            SizeBytes = processed.Jpeg.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(processed.Jpeg)).ToLowerInvariant(),
            Phash = processed.Hash.ToString("x16"),
            CreatedAt = now,
        };
        await blobs.PutAsync(media.StorageKey, processed.Jpeg, media.MimeType, ct);
        return ServiceResult<Media>.Success(media);
    }
}
