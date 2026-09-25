using OpenQuest.Api.Publishing;
using OpenQuest.Api.Queries;
using OpenQuest.Api.Sync;

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
        var admin = app.MapGroup("/admin/sync").RequireAuthorization("Admin").WithTags("Admin");

        admin.MapPost("", (ISyncTrigger sync) =>
                sync.TryStartInBackground()
                    ? Results.Accepted("/admin/sync/status", new { started = true })
                    : Results.Json(new { error = "sync_already_running" }, statusCode: 409))
            .WithName("StartSync")
            .WithSummary("Starts an asset import from the active data source adapter in the background.");

        admin.MapGet("/status", async (ISyncStatus status, IAdapterProvider adapters, IPublicationOverview overview, CancellationToken ct) =>
            Results.Ok(new { running = status.IsRunning, adapter = adapters.Active.Id, runs = await overview.SyncRunsAsync(10, ct) }))
            .WithName("SyncStatus");
    }
}
