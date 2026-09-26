using OpenQuest.Api.Districts;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Features;

/// <summary>What players (and the map) read: cities, their districts and the district leaderboard.</summary>
public static class CityEndpoints
{
    private static IResult InvalidPeriod() => Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["all or week"] });

    public static void MapCities(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("").RequireAuthorization().WithTags("Cities & districts");

        g.MapGet("/cities", async (IDistrictDirectory directory, CancellationToken ct) => Results.Ok(await directory.ListCitiesAsync(includeInactive: false, ct)))
            .WithName("ListCities")
            .WithSummary("Active cities with map centre, zoom, time zone and number of districts.");

        g.MapGet("/cities/{idOrKey}", async (string idOrKey, IDistrictDirectory directory, CancellationToken ct) =>
            await directory.GetCityAsync(idOrKey, ct) is { IsActive: true } city ? Results.Ok(city) : Results.NotFound())
            .WithName("GetCity").WithSummary("One city by id or key (for example muenster).");

        g.MapGet("/cities/{id:guid}/districts", async (Guid id, bool? geometry, IDistrictDirectory directory, CancellationToken ct) =>
            await directory.ListDistrictsAsync(id, geometry ?? false, includeInactive: false, ct) is { } list ? Results.Ok(list) : Results.NotFound())
            .WithName("ListDistricts")
            .WithSummary("The active districts of a city. With geometry=true each district carries its outline as a GeoJSON Polygon ([lon, lat], the points in drawn order).");

        // registered before {id} so that "lookup" is not read as an id
        g.MapGet("/districts/lookup", async (double lat, double lon, IDistrictDirectory directory, CancellationToken ct) =>
            lat is < -90 or > 90 || lon is < -180 or > 180
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["lat/lon"] = ["Invalid position."] })
                : await directory.LookupAsync(lat, lon, ct) is { } district ? Results.Ok(district) : Results.NotFound(new { error = "no_district" }))
            .WithName("LookupDistrict")
            .WithSummary("The district a position lies in (404 with error 'no_district' if it lies in none).");

        g.MapGet("/districts/{id:guid}", async (Guid id, IDistrictDirectory directory, CancellationToken ct) =>
            await directory.GetDistrictAsync(id, includeInactive: false, ct) is { } district ? Results.Ok(district) : Results.NotFound())
            .WithName("GetDistrict")
            .WithSummary("One district with description, outline (GeoJSON), points, rank in its city and number of contributors.");

        g.MapGet("/cities/{id:guid}/leaderboard", async (Guid id, string? period, ILeaderboards leaderboards, CancellationToken ct) =>
            !LeaderboardWindow.TryParse(period, out var p) ? InvalidPeriod()
                : await leaderboards.DistrictRankingAsync(id, p, ct) is { } ranking ? Results.Ok(ranking) : Results.NotFound())
            .WithName("CityLeaderboard")
            .WithSummary("Ranking of the city's districts by the points earned in them, best first. period = all (default) or week (Monday to Sunday in the city's time zone).");

        g.MapGet("/districts/{id:guid}/leaderboard", async (Guid id, string? period, int? limit, ILeaderboards leaderboards, CancellationToken ct) =>
            !LeaderboardWindow.TryParse(period, out var p) ? InvalidPeriod()
                : await leaderboards.TopPlayersAsync(id, p, Math.Clamp(limit ?? 10, 1, 100), ct) is { } top ? Results.Ok(top) : Results.NotFound())
            .WithName("DistrictLeaderboard")
            .WithSummary("The players who earned the most points in a district.");
    }
}
