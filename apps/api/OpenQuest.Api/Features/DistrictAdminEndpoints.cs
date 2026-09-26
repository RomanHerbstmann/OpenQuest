using System.Security.Claims;
using OpenQuest.Api.Auth;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Districts;
using OpenQuest.Api.Services;

namespace OpenQuest.Api.Features;

/// <summary>The admin panel's side: define cities and draw their districts.</summary>
public static class DistrictAdminEndpoints
{
    public static void MapDistrictAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/admin").RequireAuthorization("Admin").WithTags("Admin: cities & districts");

        g.MapGet("/cities", async (IDistrictDirectory directory, CancellationToken ct) => Results.Ok(await directory.ListCitiesAsync(includeInactive: true, ct)))
            .WithName("AdminListCities").WithSummary("All cities, including inactive ones.");

        g.MapPost("/cities", async (CityRequest req, ICityAdmin cities, CancellationToken ct) =>
            (await cities.CreateAsync(req, ct)).ToHttp(c => Results.Created($"/cities/{c.Id}", c)))
            .WithName("CreateCity")
            .WithSummary("Creates a city. name is required; key defaults to a slug of the name; timezone defaults to Europe/Berlin.");

        g.MapPut("/cities/{id:guid}", async (Guid id, CityRequest req, ICityAdmin cities, CancellationToken ct) =>
            (await cities.UpdateAsync(id, req, ct)).ToHttp(Results.Ok))
            .WithName("UpdateCity").WithSummary("Changes name, country, map centre/zoom, time zone or isActive. The key never changes.");

        g.MapDelete("/cities/{id:guid}", async (Guid id, ICityAdmin cities, CancellationToken ct) =>
            (await cities.DeleteAsync(id, ct)).ToHttp(_ => Results.NoContent()))
            .WithName("DeleteCity").WithSummary("Deletes a city without districts (409 city_has_districts otherwise).");

        g.MapGet("/cities/{id:guid}/districts", async (Guid id, bool? geometry, IDistrictDirectory directory, CancellationToken ct) =>
            await directory.ListDistrictsAsync(id, geometry ?? false, includeInactive: true, ct) is { } list ? Results.Ok(list) : Results.NotFound())
            .WithName("AdminListDistricts").WithSummary("All districts of a city, including deactivated ones.");

        g.MapGet("/districts/{id:guid}", async (Guid id, IDistrictDirectory directory, CancellationToken ct) =>
            await directory.GetDistrictAsync(id, includeInactive: true, ct) is { } district ? Results.Ok(district) : Results.NotFound())
            .WithName("AdminGetDistrict");

        g.MapPost("/cities/{id:guid}/districts", async (Guid id, DistrictRequest req, ClaimsPrincipal user, IDistrictAdmin districts, CancellationToken ct) =>
            (await districts.CreateAsync(id, user.GetUserId(), req, ct)).ToHttp(d => Results.Created($"/districts/{d.Id}", d)))
            .WithName("CreateDistrict")
            .WithSummary("Creates a district. name and geometry are required; geometry is a GeoJSON Polygon (or Feature) with [lon, lat] coordinates. The order of the points is kept and closes back to the first point; edges must not cross and the district must not overlap another one of the city (422 invalid_geometry with details.problems).");

        g.MapPost("/cities/{id:guid}/districts/validate", async (Guid id, GeometryRequest req, IDistrictAdmin districts, CancellationToken ct) =>
            (await districts.ValidateAsync(id, req, ct)).ToHttp(Results.Ok))
            .WithName("ValidateDistrictGeometry")
            .WithSummary("Dry run for the drawing tool: reports crossing edges (with the crossing point), overlaps with other districts and other problems without saving. Pass districtId when redrawing an existing district.");

        g.MapPost("/cities/{id:guid}/districts/import", async (Guid id, DistrictImportRequest req, ClaimsPrincipal user, IDistrictAdmin districts, CancellationToken ct) =>
            (await districts.ImportAsync(id, user.GetUserId(), req, ct)).ToHttp(r => Results.Created($"/cities/{id}/districts", r)))
            .WithName("ImportDistricts")
            .WithSummary("Creates one district per feature of a GeoJSON FeatureCollection (Polygons without holes). nameProperty names the property with the district name; keyProperty and descriptionProperty are optional. All or nothing: on problems nothing is saved and details.features lists them per feature.");

        g.MapPut("/districts/{id:guid}", async (Guid id, DistrictRequest req, IDistrictAdmin districts, CancellationToken ct) =>
            (await districts.UpdateAsync(id, req, ct)).ToHttp(Results.Ok))
            .WithName("UpdateDistrict")
            .WithSummary("Changes name, description, colour or isActive (deactivate instead of deleting to keep the leaderboard history). The outline is changed with PUT .../geometry.");

        g.MapPut("/districts/{id:guid}/geometry", async (Guid id, GeometryRequest req, IDistrictAdmin districts, CancellationToken ct) =>
            (await districts.ReplaceGeometryAsync(id, req, ct)).ToHttp(Results.Ok))
            .WithName("ReplaceDistrictGeometry")
            .WithSummary("Replaces the whole outline with a new GeoJSON Polygon. Points already awarded keep the district they were earned in.");

        g.MapDelete("/districts/{id:guid}", async (Guid id, IDistrictAdmin districts, CancellationToken ct) =>
            (await districts.DeleteAsync(id, ct)).ToHttp(_ => Results.NoContent()))
            .WithName("DeleteDistrict")
            .WithSummary("Deletes a district in which no points were earned (409 district_has_points otherwise).");
    }
}
