using System.Security.Claims;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Gamification;

namespace OpenQuest.Api.Features;

public static class GamificationEndpoints
{
    public static void MapGamification(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("").RequireAuthorization().WithTags("Gamification");

        g.MapGet("/me/points", async (int? offset, int? limit, ClaimsPrincipal user, IPlayerProgress progress, CancellationToken ct) =>
            Results.Ok(await progress.ListPointsAsync(user.GetUserId(), Math.Max(0, offset ?? 0), Math.Clamp(limit ?? 50, 1, 100), ct)))
            .WithName("MyPoints")
            .WithSummary("The player's points ledger, newest first. Each line says why points were given (e.g. an approved quest).");
    }
}
