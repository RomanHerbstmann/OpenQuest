using System.Security.Claims;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Badges;
using OpenQuest.Api.Contracts;

namespace OpenQuest.Api.Features;

/// <summary>Badges: what players see and earn, and what admins define (ADR-0012).</summary>
public static class BadgeEndpoints
{
    public static void MapBadges(this IEndpointRouteBuilder app)
    {
        var player = app.MapGroup("").RequireAuthorization().WithTags("Badges");

        player.MapGet("/me/badges", async (ClaimsPrincipal user, IBadgeDirectory badges, CancellationToken ct) =>
            Results.Ok(await badges.ForPlayerAsync(user.GetUserId(), ct)))
            .WithName("MyBadges")
            .WithSummary("Every active badge with the player's state: earned (with the date) or the progress towards it (current, required, percent). Earned first, then the closest ones.");

        player.MapGet("/badges", async (IBadgeDirectory badges, CancellationToken ct) => Results.Ok(await badges.ListAsync(includeInactive: false, ct)))
            .WithName("ListBadges")
            .WithSummary("The catalog of active badges: key, name and description (translation keys for the default badges), icon, criteria, bonus points.");

        var admin = app.MapGroup("/admin/badges").RequireAuthorization("Admin").WithTags("Admin: badges");

        admin.MapGet("", async (IBadgeDirectory badges, CancellationToken ct) => Results.Ok(await badges.ListAsync(includeInactive: true, ct)))
            .WithName("AdminListBadges").WithSummary("All badges, inactive ones included, with the number of players who hold them.");

        admin.MapPost("", async (BadgeRequest req, IBadgeDirectory badges, CancellationToken ct) =>
            (await badges.CreateAsync(req, ct)).ToHttp(b => Results.Created($"/admin/badges/{b.Id}", b)))
            .WithName("CreateBadge")
            .WithSummary("Adds a badge. Criteria types: approved_submissions, points, cards, distinct_genera, new_trees (count), rarity_cards (rarity + count), task_type (taskType + count). Players who have already reached it get it right away.");

        admin.MapPut("/{id:guid}", async (Guid id, BadgeRequest req, IBadgeDirectory badges, CancellationToken ct) =>
            (await badges.UpdateAsync(id, req, ct)).ToHttp(Results.Ok))
            .WithName("UpdateBadge")
            .WithSummary("Changes name, description, icon, criteria, bonus or isActive (deactivate instead of deleting: players keep what they earned). The key never changes.");
    }
}
