using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
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

        admin.MapPost("/publications/retry", async (IChangeRepublisher republisher, CancellationToken ct) =>
            Results.Ok(new { requeued = await republisher.RequeueAsync(ct) }))
            .WithName("RetryPublications")
            .WithSummary("Re-emits events for accepted changes that were not published yet (for example after dead outbox messages).");

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
    }
}
