using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Api.Publishing;
using OpenQuest.Api.Storage;
using OpenQuest.Api.Queries;
using System.Security.Claims;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Services;
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
        var admin = app.MapGroup("/admin").RequireAuthorization("Admin").WithTags("Admin");

        admin.MapGet("/sync/runs", async (IPublicationOverview overview, CancellationToken ct) =>
            Results.Ok(await overview.SyncRunsAsync(20, ct)))
            .WithName("ListSyncRuns")
            .WithSummary("Recent import runs, including failed ones with their error.")
            .WithDescription("Read only: the API does not import data itself; imports are run by the importer.");

        admin.MapPost("/sync", async (SyncRequestBody? body, ClaimsPrincipal user, ISyncRequests requests, CancellationToken ct) =>
            (await requests.RequestAsync(user.GetUserId(), body ?? new SyncRequestBody(null, null, null), ct))
                .ToHttp(list => Results.Accepted("/admin/sync/requests", list)))
            .WithName("RequestSync")
            .WithSummary("Asks the importer to sync now. Body: { \"sources\": [\"de-muenster-trees\"], \"force\": false, \"acceptSchemaChange\": false }; leave sources out for all enabled sources.")
            .WithDescription("The API does not import anything itself: the request is recorded and the importer (`serve`) picks it up, runs the sync and writes the outcome back. Poll GET /admin/sync/requests for the status. force applies a sync that would remove more assets than allowed, acceptSchemaChange imports although the source's fields changed; use both only after looking at the failed run. 409 already_requested while a request for the source is waiting or running.");

        admin.MapGet("/sync/requests", async (int? limit, ISyncRequests requests, CancellationToken ct) =>
            Results.Ok(await requests.ListAsync(Math.Clamp(limit ?? 20, 1, 100), ct)))
            .WithName("ListSyncRequests")
            .WithSummary("Recent sync requests with their status (pending, running, succeeded, failed), the run they made and the error.");

        admin.MapGet("/sync/runs/{id:guid}/snapshot", async (Guid id, ISyncSnapshots snapshots, HttpContext http, CancellationToken ct) =>
        {
            var result = await snapshots.OpenAsync(id, ct);
            if (result.Error is { } e) return Results.Json(new { error = e.Code, message = e.Message }, statusCode: e.Status);
            var download = result.Value;
            return Results.Stream(async stream => await download.WriteAsync(stream, ct), download.ContentType, download.FileName);
        })
            .WithName("DownloadSyncSnapshot")
            .WithSummary("The raw download of a run, unchanged as the source delivered it (a zip with the main file and the reference files if the run has any).")
            .WithDescription("Only available when the importer keeps its snapshots in S3 (`[snapshots] backend = \"s3\"`); 404 snapshot_unavailable otherwise.");

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
