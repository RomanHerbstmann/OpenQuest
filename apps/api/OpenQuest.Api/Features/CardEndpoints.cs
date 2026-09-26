using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Cards;
using OpenQuest.Api.Data;

namespace OpenQuest.Api.Features;

/// <summary>Tree cards: the player's cards and tree book, and how frequent the genera of a district are (what decides how rare a card is).</summary>
public static class CardEndpoints
{
    public static void MapCards(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("").RequireAuthorization().WithTags("Cards");

        g.MapGet("/me/cards", async (int? offset, int? limit, ClaimsPrincipal user, ICardCollection cards, CancellationToken ct) =>
            Results.Ok(await cards.ListAsync(user.GetUserId(), Math.Max(0, offset ?? 0), Math.Clamp(limit ?? 50, 1, 200), ct)))
            .WithName("MyCards")
            .WithSummary("The player's cards, newest first. rarity: common, uncommon, rare, legendary. reasons say why the chances were good: scarce_in_district, new_to_district, new_information, condition_fact.");

        g.MapGet("/me/collection", async (ClaimsPrincipal user, ICardCollection cards, CancellationToken ct) =>
            Results.Ok(await cards.CollectionAsync(user.GetUserId(), ct)))
            .WithName("MyCollection")
            .WithSummary("The tree book: every genus collected with count and best rarity, plus totals per rarity.");

        g.MapGet("/districts/{id:guid}/genera", async (Guid id, int? limit, IDistrictGenusStats stats, CancellationToken ct) =>
            await stats.ListAsync(id, Math.Clamp(limit ?? 50, 1, 500), includeInactive: false, ct) is { } result ? Results.Ok(result) : Results.NotFound())
            .WithName("DistrictGenera")
            .WithSummary("Which genera grow in a district and how many: most frequent first, with share and frequency class. The scarcer a genus, the better the chance of a rare card there.");

        app.MapPost("/admin/districts/{id:guid}/genera/refresh", async (Guid id, IDistrictGenusStats stats, AppDbContext db, CancellationToken ct) =>
            !await db.Districts.AnyAsync(d => d.Id == id, ct) ? Results.NotFound() : Results.Ok(new { genera = await stats.RefreshAsync(id, ct) }))
            .RequireAuthorization("Admin").WithTags("Admin: cities & districts")
            .WithName("RefreshDistrictGenera")
            .WithSummary("Recalculates the genus statistics of a district now (they are recalculated automatically when old or after redrawing).");
    }
}
