using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Api.Storage;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Features;

public static class MediaEndpoints
{
    public static void MapMedia(this IEndpointRouteBuilder app)
    {
        // Photos are only public once a moderator approved the submission (review before publishing).
        app.MapGet("/media/{id:guid}", async (Guid id, AppDbContext db, IBlobReader blobs, CancellationToken ct) =>
        {
            var key = await db.Media.AsNoTracking()
                .Where(m => m.Id == id && m.Submission.Status == SubmissionStatus.Approved)
                .Select(m => m.StorageKey).FirstOrDefaultAsync(ct);
            if (key is null) return Results.NotFound();
            var bytes = await blobs.GetAsync(key, ct);
            return bytes is null ? Results.NotFound() : Results.File(bytes, "image/jpeg");
        }).WithTags("Player").WithName("PublicMedia").WithSummary("An approved photo (public).");

        app.MapGet("/admin/media/{id:guid}", async (Guid id, AppDbContext db, IBlobReader blobs, CancellationToken ct) =>
        {
            var key = await db.Media.AsNoTracking().Where(m => m.Id == id).Select(m => m.StorageKey).FirstOrDefaultAsync(ct);
            if (key is null) return Results.NotFound();
            var bytes = await blobs.GetAsync(key, ct);
            return bytes is null ? Results.NotFound() : Results.File(bytes, "image/jpeg");
        }).RequireAuthorization("Moderator").WithTags("Moderation")
          .WithName("ModerationMedia").WithSummary("Any submission photo, also before review.");
    }
}
