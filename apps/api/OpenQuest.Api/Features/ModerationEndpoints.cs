using System.Security.Claims;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Contracts;
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

        g.MapPost("/submissions/{id:guid}/review", async (Guid id, ReviewRequest req, ClaimsPrincipal user,
                ISubmissionReviewService reviews, CancellationToken ct) =>
            (await reviews.ReviewAsync(user.GetUserId(), id, req.Approved, req.Reason, ct))
                .ToHttp(status => Results.Ok(new { id, status })))
            .WithName("ReviewSubmission")
            .WithSummary("Approve or reject a pending submission. Approval publishes the accepted change to open data right away; rejecting requires a reason and frees the quest slot.");
    }
}
