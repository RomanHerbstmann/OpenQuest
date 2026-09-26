using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Config;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Gamification;
using OpenQuest.Api.Queries;
using OpenQuest.Api.Services;

namespace OpenQuest.Api.Features;

public static class PlayerEndpoints
{
    private static IResult InvalidPosition()
        => Results.ValidationProblem(new Dictionary<string, string[]> { ["lat/lon"] = ["Invalid position."] });

    private static bool ValidPosition(double lat, double lon) => lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    public static void MapPlayer(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("").RequireAuthorization().WithTags("Player");

        g.MapGet("/me", async (ClaimsPrincipal user, IPlayerProgress progress, CancellationToken ct) =>
            {
                var p = await progress.GetAsync(user.GetUserId(), ct);
                return Results.Ok(new
                {
                    id = user.GetUserId(), username = user.FindFirst("unique_name")?.Value, role = user.FindFirst("role")?.Value,
                    totalPoints = p.TotalPoints, level = p.Level,
                });
            })
            .WithName("Me")
            .WithSummary("The signed-in user with total points and level progress (level, current, required, percent).");

        g.MapGet("/quests/nearby", async (double lat, double lon, double? radius, string? taskType, ClaimsPrincipal user,
                INearbyQuests quests, CancellationToken ct) =>
            ValidPosition(lat, lon) ? Results.Ok(await quests.FindAsync(lat, lon, radius, taskType, user.GetUserId(), ct)) : InvalidPosition())
            .WithName("NearbyQuests")
            .WithSummary("Open quests near a position, nearest first. Full quests and quests the user already holds are hidden.");

        g.MapGet("/assets/nearby", async (double lat, double lon, double? radius, string? assetType, INearbyAssets assets, CancellationToken ct) =>
            ValidPosition(lat, lon) ? Results.Ok(await assets.FindAsync(lat, lon, radius, assetType, ct)) : InvalidPosition())
            .WithName("NearbyAssets")
            .WithSummary("Assets (e.g. all trees) near a position, for showing them on the map.");

        g.MapPost("/quests/{id:guid}/claim", async (Guid id, ClaimsPrincipal user, IQuestClaimService claims, CancellationToken ct) =>
            (await claims.ClaimAsync(user.GetUserId(), id, ct))
                .ToHttp(c => Results.Created("/me/claims", new ClaimDto(c.Id, c.QuestId, c.Status, c.ClaimedAt, c.ExpiresAt))))
            .WithName("ClaimQuest")
            .WithSummary("Reserves a slot on a quest. The claim expires after the quest's claim_ttl_minutes.")
            .Produces<ClaimDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict);

        g.MapPost("/claims/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, IQuestClaimService claims, CancellationToken ct) =>
            (await claims.CancelAsync(user.GetUserId(), id, ct)).ToHttp(_ => Results.NoContent()))
            .WithName("CancelClaim");

        g.MapPost("/claims/{id:guid}/submit", async (Guid id, HttpRequest req, ClaimsPrincipal user,
                ISubmissionService submissions, IOptions<StorageOptions> storage, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.Json(new { error = "multipart_required" }, statusCode: 415);
            var form = await req.ReadFormAsync(ct);

            if (!double.TryParse(form["lat"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(form["lon"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                return Results.Json(new { error = "invalid_position", message = "Form fields 'lat' and 'lon' are required." }, statusCode: 400);

            JsonNode? payload = new JsonObject();
            if (form.TryGetValue("payload", out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                try { payload = JsonNode.Parse(raw.ToString()); }
                catch (JsonException) { return Results.Json(new { error = "invalid_payload", message = "'payload' must be a JSON object." }, statusCode: 400); }
            }

            byte[]? photo = null;
            var file = form.Files.GetFile("photo");
            if (file is { Length: > 0 })
            {
                if (file.Length > storage.Value.MaxPhotoBytes)
                    return Results.Json(new { error = "photo_too_large", maxBytes = storage.Value.MaxPhotoBytes }, statusCode: 413);
                using var ms = new MemoryStream((int)file.Length);
                await file.CopyToAsync(ms, ct);
                photo = ms.ToArray();
            }

            return (await submissions.SubmitAsync(user.GetUserId(), id, new SubmitInput(lat, lon, payload, photo), ct))
                .ToHttp(r => Results.Created("/me/claims", r));
        })
        .DisableAntiforgery()
        .WithName("SubmitClaim")
        .WithSummary("Completes a claim. multipart/form-data: lat, lon (reported GPS position), payload (JSON object), photo (image).")
        .WithDescription("payload per task type — photo: {} (photo required); verify_attribute: {\"value\":\"Tilia\"}; measure: {\"value\":123.5}; condition_report: {\"condition\":\"good|damaged|dead|gone\"}. Must be within the quest's geofence radius of the asset. See task_type.result_schema.")
        .Accepts<IFormFile>("multipart/form-data", "application/x-www-form-urlencoded");

        g.MapGet("/me/claims", async (ClaimsPrincipal user, IPlayerClaims claims, CancellationToken ct) =>
            Results.Ok(await claims.ListAsync(user.GetUserId(), ct)))
            .WithName("MyClaims");
    }
}
