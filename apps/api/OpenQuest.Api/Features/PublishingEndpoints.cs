using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenQuest.Api.Config;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;
using OpenQuest.Api.Publishing;
using OpenQuest.Api.Storage;
using OpenQuest.Api.Queries;

namespace OpenQuest.Api.Features;

public static class PublishingEndpoints
{
    public static void MapPublishing(this IEndpointRouteBuilder app)
    {
        // The stable URL the city can link or harvest. Updated within seconds of every approval (event-driven).
        app.MapGet("/open-data/{dataSource}/changes.{format}", async (string dataSource, string format, IPublishedFeed feed, HttpContext http, CancellationToken ct) =>
        {
            if (format is not ("geojson" or "csv")) return Results.NotFound();
            var bytes = await feed.LatestAsync(dataSource, format, ct);
            if (bytes is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=60";
            return Results.File(bytes, format == "csv" ? "text/csv; charset=utf-8" : "application/geo+json");
        })
        .WithTags("Open data").WithName("PublishedChanges")
        .WithSummary("All accepted changes of a data source as GeoJSON or CSV, including the required attribution. Public.");

        var admin = app.MapGroup("/admin").RequireAuthorization("Admin").WithTags("Admin");

        admin.MapGet("/publications", async (int? limit, IPublicationOverview overview, CancellationToken ct) =>
            Results.Ok(await overview.ListRunsAsync(Math.Clamp(limit ?? 50, 1, 200), ct)))
            .WithName("ListPublications").WithSummary("Publication runs (one per delivered batch of accepted changes).");

        admin.MapPost("/publications/retry", async (IChangeRepublisher republisher, IPublishingGate gate, CancellationToken ct) =>
            !gate.Enabled
                ? Results.Json(new { error = "publishing_disabled", message = "Publishing to open data is switched off (Publishing:Enabled). Accepted changes stay in the database." }, statusCode: 409)
                : Results.Ok(new { requeued = await republisher.RequeueAsync(ct) }))
            .WithName("RetryPublications")
            .WithSummary("Re-emits events for accepted changes that were not published yet (for example after dead outbox messages, or after publishing was switched on). 409 publishing_disabled while it is off.");

        admin.MapGet("/publications/status", async (IPublishingGate gate, IOptions<PublishingOptions> options, AppDbContext db, CancellationToken ct) =>
            Results.Ok(new
            {
                enabled = gate.Enabled,
                githubConfigured = !string.IsNullOrWhiteSpace(options.Value.GitHub.Token),
                // accepted by a moderator, kept as user data, not sent anywhere
                acceptedNotPublished = await db.AttributeChanges.CountAsync(c => c.Status == ChangeStatus.Accepted, ct)
                                       + await db.AssetProposals.CountAsync(p => p.Status == ChangeStatus.Accepted, ct),
                published = await db.AttributeChanges.CountAsync(c => c.Status == ChangeStatus.Exported, ct)
                            + await db.AssetProposals.CountAsync(p => p.Status == ChangeStatus.Exported, ct),
            }))
            .WithName("PublicationStatus")
            .WithSummary("Whether publishing to open data is on, and how many accepted changes and reported trees are kept as user data (accepted) or were sent out (published).");

        admin.MapGet("/outbox", async (IPublicationOverview overview, CancellationToken ct) =>
            Results.Ok(await overview.OutboxStatusAsync(ct)))
            .WithName("OutboxStatus").WithSummary("Event delivery status: pending, processed, dead, and recent failures.");
    }
}

public static class SyncEndpoints
{
    public static void MapSync(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin").RequireAuthorization("Admin").WithTags("Admin");

        admin.MapGet("/sync/runs", async (IPublicationOverview overview, CancellationToken ct) =>
            Results.Ok(await overview.SyncRunsAsync(20, ct)))
            .WithName("ListSyncRuns")
            .WithSummary("Recent import runs, including failed ones with their error.")
            .WithDescription("Read only: the API does not import data itself; imports are run by the importer.");

        admin.MapGet("/assets/{id:guid}/history", async (Guid id, IAssetHistory history, CancellationToken ct) =>
            await history.ListAsync(id, ct) is { } versions ? Results.Ok(versions) : Results.NotFound())
            .WithName("AssetHistory")
            .WithSummary("Versions of an asset over time: created, changed (with position and source record) or removed at the source.");

        admin.MapGet("/reports", async (string? status, string? category, int? limit, IImportedFeeds feeds, CancellationToken ct) =>
            Results.Ok(await feeds.ReportsAsync(status, category, Math.Clamp(limit ?? 100, 1, 1000), ct)))
            .WithName("ListReports")
            .WithSummary("Reports from external feeds (e.g. the city's issue tracker), newest first, with the linked asset.")
            .WithDescription("Filter with status=open|closed and category (e.g. tree_damage). Create quests for the linked assets with target.withOpenReport.");

        admin.MapGet("/readings", async (string? metric, int? days, IImportedFeeds feeds, CancellationToken ct) =>
            Results.Ok(await feeds.ReadingsAsync(metric, Math.Clamp(days ?? 14, 1, 366), ct)))
            .WithName("ListReadings")
            .WithSummary("Environment readings (e.g. daily soil moisture) of the last days, newest first.");
    }
}
