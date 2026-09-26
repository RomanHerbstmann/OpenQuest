using System.Security.Claims;
using OpenQuest.Api.Auth;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Queries;
using OpenQuest.Api.Services;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Features;

public static class ModerationEndpoints
{
    public static void MapModeration(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin").RequireAuthorization("Moderator").WithTags("Moderation");

        g.MapGet("/submissions", async (string? status, int? limit, int? offset, IModerationQueue queue, CancellationToken ct) =>
        {
            var st = SubmissionStatus.Pending;
            if (status is not null && !EnumParsing.TryParseSnake(status, out st))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["pending, approved or rejected"] });
            return Results.Ok(await queue.ListAsync(st, offset ?? 0, Math.Clamp(limit ?? 50, 1, 200), ct));
        }).WithName("ListSubmissions");

        g.MapGet("/proposals", async (string? status, int? limit, int? offset, AppDbContext db, CancellationToken ct) =>
        {
            ChangeStatus? parsed = null;
            if (status is not null)
            {
                if (!EnumParsing.TryParseSnake<ChangeStatus>(status, out var cs))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["proposed, accepted, exported or discarded"] });
                parsed = cs;
            }
            var rows = await db.AssetProposals.AsNoTracking().Where(p => parsed == null || p.Status == parsed)
                .OrderByDescending(p => p.CreatedAt).Skip(offset ?? 0).Take(Math.Clamp(limit ?? 50, 1, 200))
                .Select(p => new
                {
                    p.Id, p.SubmissionId, p.Status, p.Geom, p.Genus, p.Species, p.Note, p.PhotoUrl, p.DistrictId,
                    dataSource = db.DataSources.Where(d => d.Id == p.DataSourceId).Select(d => d.Key).FirstOrDefault(), p.CreatedAt,
                }).ToListAsync(ct);
            return Results.Ok(rows.Select(p => new
            {
                p.Id, p.SubmissionId, p.Status, lat = p.Geom.Y, lon = p.Geom.X, p.Genus, p.Species, p.Note, p.PhotoUrl, p.DistrictId, p.dataSource, p.CreatedAt,
            }));
        }).WithName("ListTreeProposals")
          .WithSummary("Trees that players reported as missing in the data (task type report_new_tree). Approved ones are published with the other accepted changes as attribute new_tree.");

        g.MapPost("/submissions/{id:guid}/review", async (Guid id, ReviewRequest req, ClaimsPrincipal user,
                ISubmissionReviewService reviews, CancellationToken ct) =>
            (await reviews.ReviewAsync(user.GetUserId(), id, req.Approved, req.Reason, ct))
                .ToHttp(status => Results.Ok(new { id, status })))
            .WithName("ReviewSubmission")
            .WithSummary("Approve or reject a pending submission. Approval publishes the accepted change to open data right away; rejecting requires a reason and frees the quest slot.");
    }
}
