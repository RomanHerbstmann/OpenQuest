using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Tests;

/// <summary>
/// Cities and districts drawn by admins, and the district leaderboard. Every test works in its own patch of the map
/// (own latitude band), so districts of different tests never touch each other.
/// </summary>
[Collection(ApiCollection.Name)]
public class DistrictTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>A GeoJSON Polygon ([lon, lat], closed) for a rectangle.</summary>
    private static JsonObject Rect(double lat, double lon, double dLat = 0.01, double dLon = 0.01)
        => Ring((lat, lon), (lat, lon + dLon), (lat + dLat, lon + dLon), (lat + dLat, lon));

    private static JsonObject Ring(params (double Lat, double Lon)[] points)
    {
        var coordinates = new JsonArray();
        foreach (var (lat, lon) in points) coordinates.Add(new JsonArray(lon, lat));
        coordinates.Add(new JsonArray(points[0].Lon, points[0].Lat));
        return new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(coordinates) };
    }

    private async Task<(HttpClient Admin, Guid CityId)> NewCityAsync(string? name = null)
    {
        var admin = await api.AdminAsync();
        var res = await admin.PostAsJsonAsync("/admin/cities", new { name = name ?? "Testcity " + Guid.NewGuid().ToString("N")[..8], countryCode = "de", centerLat = 51.96, centerLon = 7.62, defaultZoom = 12 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (admin, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> Body(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateDistrictAsync(HttpClient admin, Guid cityId, string name, JsonNode geometry, string? color = null)
    {
        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name, description = "d " + name, color, geometry }, Web);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await Body(res)).GetProperty("id").GetGuid();
    }

    private static string[] ProblemCodes(JsonElement details) => details.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("code").GetString()!).ToArray();

    // ---- cities ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cities_are_created_read_updated_and_deleted_by_admins_only()
    {
        var admin = await api.AdminAsync();
        var key = "test-" + Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/admin/cities", new { key, name = "Teststadt", countryCode = "de", centerLat = 51.96, centerLon = 7.62, defaultZoom = 12, timezone = "Europe/Berlin" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var city = await Body(created);
        var id = city.GetProperty("id").GetGuid();
        Assert.Equal("DE", city.GetProperty("countryCode").GetString());
        Assert.Equal(0, city.GetProperty("districtCount").GetInt32());

        var (player, _, _) = await api.RegisterAsync("reader");
        Assert.Equal(key, (await Body(await player.GetAsync($"/cities/{id}"))).GetProperty("key").GetString());
        Assert.Equal(id, (await Body(await player.GetAsync($"/cities/{key}"))).GetProperty("id").GetGuid()); // by key
        Assert.Contains((await player.GetFromJsonAsync<JsonElement>("/cities")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);

        // only admins write, only logged-in users read
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync("/admin/cities", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PutAsJsonAsync($"/admin/cities/{id}", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.DeleteAsync($"/admin/cities/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/cities")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/cities/{id}/leaderboard")).StatusCode);

        var updated = await Body(await admin.PutAsJsonAsync($"/admin/cities/{id}", new { name = "Neue Teststadt", defaultZoom = 13, timezone = "Europe/London" }));
        Assert.Equal("Neue Teststadt", updated.GetProperty("name").GetString());
        Assert.Equal("Europe/London", updated.GetProperty("timezone").GetString());

        // deactivated cities disappear for players
        await admin.PutAsJsonAsync($"/admin/cities/{id}", new { isActive = false });
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/cities/{id}")).StatusCode);
        Assert.Contains((await admin.GetFromJsonAsync<JsonElement>("/admin/cities")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/cities/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/admin/cities/{id}")).StatusCode);
    }

    [Fact]
    public async Task City_input_is_validated_and_keys_are_unique()
    {
        var admin = await api.AdminAsync();
        var key = "dup-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/admin/cities", new { key, name = "Eins" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/admin/cities", new { key, name = "Zwei" })).StatusCode);

        foreach (var bad in new object[]
        {
            new { name = "" },
            new { name = "X", timezone = "Mars/Olympus" },
            new { name = "X", centerLat = 51.0 },              // centre needs both values
            new { name = "X", centerLat = 95.0, centerLon = 7.0 },
            new { name = "X", defaultZoom = 99 },
            new { name = "X", countryCode = "DEU" },
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/cities", bad)).StatusCode);
    }

    [Fact]
    public async Task A_city_key_is_made_from_the_name_when_not_given()
    {
        var admin = await api.AdminAsync();
        var name = "Bad Säckingen " + Guid.NewGuid().ToString("N")[..6];
        var city = await Body(await admin.PostAsJsonAsync("/admin/cities", new { name }));
        Assert.StartsWith("bad-saeckingen-", city.GetProperty("key").GetString());
    }

    // ---- districts: drawing ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_district_keeps_the_order_of_its_points_and_is_returned_as_geojson()
    {
        var (admin, cityId) = await NewCityAsync();
        // an L-shaped outline drawn clockwise, starting somewhere in the middle of an edge sequence
        var drawn = new (double Lat, double Lon)[] { (40.02, 7.0), (40.02, 7.02), (40.01, 7.02), (40.01, 7.01), (40.0, 7.01), (40.0, 7.0) };
        var geometry = Ring(drawn);
        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Ell", description = "Ein L", color = "#2c8054", geometry }, Web);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var district = await Body(res);
        Assert.Equal("ell", district.GetProperty("key").GetString());
        Assert.Equal("#2C8054", district.GetProperty("color").GetString());
        Assert.Equal(6, district.GetProperty("pointCount").GetInt32());
        Assert.True(district.GetProperty("centroidLat").GetDouble() is > 40.0 and < 40.02);

        // GeoJSON back: [lon, lat], same order as drawn, closed again
        var ring = district.GetProperty("geometry").GetProperty("coordinates")[0].EnumerateArray().ToList();
        Assert.Equal(drawn.Length + 1, ring.Count);
        for (var i = 0; i < drawn.Length; i++)
        {
            Assert.Equal(drawn[i].Lon, ring[i][0].GetDouble(), 9);
            Assert.Equal(drawn[i].Lat, ring[i][1].GetDouble(), 9);
        }
        Assert.Equal(ring[0][0].GetDouble(), ring[^1][0].GetDouble());

        // stored as ordered points
        var id = district.GetProperty("id").GetGuid();
        var stored = await api.WithDb(db => db.DistrictPoints.Where(p => p.DistrictId == id).OrderBy(p => p.Position).ToListAsync());
        Assert.Equal(Enumerable.Range(0, 6), stored.Select(p => p.Position));
        Assert.Equal(drawn.Select(d => d.Lat), stored.Select(p => p.Lat));
    }

    [Fact]
    public async Task A_geojson_feature_is_accepted_like_a_polygon_and_an_open_ring_is_closed()
    {
        var (admin, cityId) = await NewCityAsync();
        var open = new JsonObject
        {
            ["type"] = "Feature", ["properties"] = new JsonObject(),
            ["geometry"] = new JsonObject
            {
                ["type"] = "Polygon",
                ["coordinates"] = new JsonArray(new JsonArray(new JsonArray(7.0, 41.0), new JsonArray(7.01, 41.0), new JsonArray(7.01, 41.01), new JsonArray(7.0, 41.01))),
            },
        };
        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Feature", geometry = open }, Web);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.Equal(4, (await Body(res)).GetProperty("pointCount").GetInt32());
    }

    [Fact]
    public async Task Crossing_edges_are_rejected_with_the_edges_and_the_crossing_point()
    {
        var (admin, cityId) = await NewCityAsync();
        // bow tie: the same four corners in the wrong order
        var bowTie = Ring((42.0, 7.0), (42.01, 7.01), (42.0, 7.01), (42.01, 7.0));

        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Schleife", geometry = bowTie }, Web);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await Body(res);
        Assert.Equal("invalid_geometry", body.GetProperty("error").GetString());
        var problem = body.GetProperty("details").GetProperty("problems")[0];
        Assert.Equal("self_intersection", problem.GetProperty("code").GetString());
        Assert.Equal(0, problem.GetProperty("edgeA").GetInt32());
        Assert.Equal(2, problem.GetProperty("edgeB").GetInt32());
        Assert.Equal(42.005, problem.GetProperty("lat").GetDouble(), 6);
        Assert.Equal(7.005, problem.GetProperty("lon").GetDouble(), 6);

        // nothing was saved
        Assert.Empty(await admin.GetFromJsonAsync<List<JsonElement>>($"/admin/cities/{cityId}/districts") ?? []);
    }

    [Theory]
    [InlineData("""{"type":"Point","coordinates":[7,42]}""", "unsupported_geometry")]
    [InlineData("""{"type":"MultiPolygon","coordinates":[]}""", "unsupported_geometry")]
    [InlineData("""{"type":"Polygon","coordinates":[]}""", "invalid_geometry")]
    [InlineData("""{"type":"Polygon","coordinates":[[[7,42],[7.1,42],[7.1,42.1],[7,42.1],[7,42]],[[7.02,42.02],[7.04,42.02],[7.04,42.04],[7.02,42.02]]]}""", "holes_not_supported")]
    [InlineData("""{"type":"Polygon","coordinates":[[[7,42],[7.1,42]]]}""", "too_few_points")]
    [InlineData("""{"type":"Polygon","coordinates":[[[7,42],["x",42],[7.1,42.1]]]}""", "invalid_geometry")]
    [InlineData("""{"type":"Polygon","coordinates":[[[7,200],[7.1,42],[7.1,42.1]]]}""", "invalid_coordinate")]
    public async Task Unusable_geometries_are_rejected_with_a_clear_code(string geometryJson, string code)
    {
        var (admin, cityId) = await NewCityAsync();
        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Kaputt", geometry = JsonNode.Parse(geometryJson) }, Web);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Contains(code, ProblemCodes((await Body(res)).GetProperty("details")));
    }

    [Fact]
    public async Task Geometry_and_name_are_required()
    {
        var (admin, cityId) = await NewCityAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Ohne Form" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { geometry = Rect(43, 7) }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Farbe", color = "green", geometry = Rect(43, 7) }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/admin/cities/{Guid.NewGuid()}/districts", new { name = "X", geometry = Rect(43, 7) }, Web)).StatusCode);
    }

    [Fact]
    public async Task Districts_of_a_city_may_share_a_border_but_not_an_area()
    {
        var (admin, cityId) = await NewCityAsync();
        await CreateDistrictAsync(admin, cityId, "West", Rect(44.0, 7.0));

        var east = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Ost", geometry = Rect(44.0, 7.01) }, Web); // shares the edge lon 7.01
        Assert.Equal(HttpStatusCode.Created, east.StatusCode);

        var overlapping = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Mitte", geometry = Rect(44.0, 7.005) }, Web);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, overlapping.StatusCode);
        var problem = (await Body(overlapping)).GetProperty("details").GetProperty("problems")[0];
        Assert.Equal("overlaps_district", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("overlapRatio").GetDouble() > 0.4);
        Assert.Contains(problem.GetProperty("districtName").GetString(), new[] { "West", "Ost" });

        // a district inside another one overlaps as well
        var inside = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Innen", geometry = Rect(44.002, 7.002, 0.002, 0.002) }, Web);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inside.StatusCode);
    }

    [Fact]
    public async Task Districts_of_different_cities_do_not_block_each_other()
    {
        var (admin, cityA) = await NewCityAsync();
        var (_, cityB) = await NewCityAsync();
        await CreateDistrictAsync(admin, cityA, "Gleich", Rect(45.0, 7.0));
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/admin/cities/{cityB}/districts", new { name = "Gleich", geometry = Rect(45.0, 7.0) }, Web)).StatusCode);
    }

    [Fact]
    public async Task Districts_without_a_colour_get_different_map_colours()
    {
        var (admin, cityId) = await NewCityAsync();
        var first = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Eins", geometry = Rect(62.0, 7.0) }, Web));
        var second = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "Zwei", geometry = Rect(62.0, 7.02) }, Web));
        Assert.Matches("^#[0-9A-F]{6}$", first.GetProperty("color").GetString());
        Assert.NotEqual(first.GetProperty("color").GetString(), second.GetProperty("color").GetString());

        // imported districts are coloured too, and go on with the next colours
        var imported = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", new
        {
            geoJson = Collection(Feature("Drei", Rect(62.0, 7.04)), Feature("Vier", Rect(62.0, 7.06))), nameProperty = "NAME",
        }, Web));
        var colours = imported.GetProperty("districts").EnumerateArray().Select(d => d.GetProperty("color").GetString()).ToList();
        Assert.Equal(2, colours.Distinct().Count());
        Assert.DoesNotContain(first.GetProperty("color").GetString(), colours);
    }

    [Fact]
    public async Task Names_are_unique_within_a_city_ignoring_case()
    {
        var (admin, cityId) = await NewCityAsync();
        await CreateDistrictAsync(admin, cityId, "Hafen", Rect(46.0, 7.0));
        var again = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts", new { name = "HAFEN", geometry = Rect(46.5, 7.0) }, Web);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("name_taken", (await Body(again)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_dry_run_reports_problems_without_saving_anything()
    {
        var (admin, cityId) = await NewCityAsync();
        var existing = await CreateDistrictAsync(admin, cityId, "Alt", Rect(47.0, 7.0));

        var ok = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/validate", new { geometry = Rect(47.0, 7.01) }, Web));
        Assert.True(ok.GetProperty("valid").GetBoolean());
        Assert.Equal(4, ok.GetProperty("pointCount").GetInt32());
        Assert.True(ok.GetProperty("centroidLat").GetDouble() > 47.0);

        var crossing = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/validate",
            new { geometry = Ring((47.5, 7.0), (47.51, 7.01), (47.5, 7.01), (47.51, 7.0)) }, Web));
        Assert.False(crossing.GetProperty("valid").GetBoolean());
        Assert.Equal("self_intersection", crossing.GetProperty("problems")[0].GetProperty("code").GetString());

        var overlap = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/validate", new { geometry = Rect(47.0, 7.005) }, Web));
        Assert.Equal("overlaps_district", overlap.GetProperty("problems")[0].GetProperty("code").GetString());
        // the same outline is fine when it is the district being redrawn
        var redraw = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/validate", new { geometry = Rect(47.0, 7.005), districtId = existing }, Web));
        Assert.True(redraw.GetProperty("valid").GetBoolean());

        var notAPolygon = await Body(await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/validate", new { geometry = JsonNode.Parse("""{"type":"Point","coordinates":[7,47]}""") }, Web));
        Assert.False(notAPolygon.GetProperty("valid").GetBoolean());

        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/admin/cities/{cityId}/districts")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/admin/cities/{Guid.NewGuid()}/districts/validate", new { geometry = Rect(47, 7) }, Web)).StatusCode);
    }

    // ---- districts: changing ------------------------------------------------------------------------------------

    [Fact]
    public async Task Redrawing_replaces_all_points_and_the_centre_and_keeps_the_order()
    {
        var (admin, cityId) = await NewCityAsync();
        var id = await CreateDistrictAsync(admin, cityId, "Umriss", Rect(48.0, 7.0));
        var before = await Body(await admin.GetAsync($"/admin/districts/{id}"));

        var newShape = Ring((48.1, 7.1), (48.1, 7.13), (48.12, 7.15), (48.14, 7.13), (48.14, 7.1)); // 5 points
        var res = await admin.PutAsJsonAsync($"/admin/districts/{id}/geometry", new { geometry = newShape }, Web);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var after = await Body(res);
        Assert.Equal(5, after.GetProperty("pointCount").GetInt32());
        Assert.NotEqual(before.GetProperty("centroidLat").GetDouble(), after.GetProperty("centroidLat").GetDouble());
        Assert.Equal(48.1, after.GetProperty("geometry").GetProperty("coordinates")[0][0][1].GetDouble(), 9);

        var stored = await api.WithDb(db => db.DistrictPoints.Where(p => p.DistrictId == id).OrderBy(p => p.Position).Select(p => p.Position).ToListAsync());
        Assert.Equal(Enumerable.Range(0, 5), stored);

        // redrawing over itself is fine; a crossing redraw is rejected and leaves the old shape
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/admin/districts/{id}/geometry", new { geometry = Rect(48.1, 7.1, 0.05, 0.05) }, Web)).StatusCode);
        var bad = await admin.PutAsJsonAsync($"/admin/districts/{id}/geometry", new { geometry = Ring((48.0, 7.0), (48.01, 7.01), (48.0, 7.01), (48.01, 7.0)) }, Web);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal(4, (await Body(await admin.GetAsync($"/admin/districts/{id}"))).GetProperty("pointCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/admin/districts/{Guid.NewGuid()}/geometry", new { geometry = Rect(48, 7) }, Web)).StatusCode);
    }

    [Fact]
    public async Task Name_description_and_colour_can_change_and_a_district_can_be_deactivated_and_reactivated()
    {
        var (admin, cityId) = await NewCityAsync();
        var id = await CreateDistrictAsync(admin, cityId, "Alt", Rect(49.0, 7.0));
        var (player, _, _) = await api.RegisterAsync("viewer");

        var res = await Body(await admin.PutAsJsonAsync($"/admin/districts/{id}", new { name = "Neu", description = "Neu beschrieben", color = "#112233" }, Web));
        Assert.Equal("Neu", res.GetProperty("name").GetString());
        Assert.Equal("alt", res.GetProperty("key").GetString()); // the key stays
        Assert.Equal("#112233", res.GetProperty("color").GetString());

        await admin.PutAsJsonAsync($"/admin/districts/{id}", new { isActive = false }, Web);
        Assert.Empty(await player.GetFromJsonAsync<List<JsonElement>>($"/cities/{cityId}/districts") ?? []);
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/districts/{id}")).StatusCode);
        Assert.Single(await admin.GetFromJsonAsync<List<JsonElement>>($"/admin/cities/{cityId}/districts") ?? []);

        // while it is off, someone else may draw over it; then it cannot come back
        await CreateDistrictAsync(admin, cityId, "Ersatz", Rect(49.0, 7.0));
        var back = await admin.PutAsJsonAsync($"/admin/districts/{id}", new { isActive = true }, Web);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, back.StatusCode);
    }

    [Fact]
    public async Task Players_read_districts_with_and_without_geometry_and_can_look_a_position_up()
    {
        var (admin, cityId) = await NewCityAsync();
        var id = await CreateDistrictAsync(admin, cityId, "Sichtbar", Rect(50.0, 7.0), "#AA0000");
        var (player, _, _) = await api.RegisterAsync("mapreader");

        var plain = (await player.GetFromJsonAsync<List<JsonElement>>($"/cities/{cityId}/districts"))!.Single();
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("geometry").ValueKind);
        Assert.Equal("#AA0000", plain.GetProperty("color").GetString());
        var withGeometry = (await player.GetFromJsonAsync<List<JsonElement>>($"/cities/{cityId}/districts?geometry=true"))!.Single();
        Assert.Equal("Polygon", withGeometry.GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(1, (await player.GetFromJsonAsync<JsonElement>($"/cities/{cityId}")).GetProperty("districtCount").GetInt32());

        var detail = await player.GetFromJsonAsync<JsonElement>($"/districts/{id}");
        Assert.Equal("d Sichtbar", detail.GetProperty("description").GetString());
        Assert.Equal(1, detail.GetProperty("rank").GetInt32());
        Assert.Equal(0, detail.GetProperty("contributors").GetInt32());

        var inside = await player.GetAsync("/districts/lookup?lat=50.005&lon=7.005");
        Assert.Equal(id, (await Body(inside)).GetProperty("id").GetGuid());
        var outside = await player.GetAsync("/districts/lookup?lat=50.5&lon=7.5");
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.Equal("no_district", (await Body(outside)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await player.GetAsync("/districts/lookup?lat=95&lon=7")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync($"/cities/{Guid.NewGuid()}/districts")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_change_districts()
    {
        var (admin, cityId) = await NewCityAsync();
        var id = await CreateDistrictAsync(admin, cityId, "Geschuetzt", Rect(50.3, 7.0));
        var (player, _, _) = await api.RegisterAsync("intruder");
        var body = new { name = "Hack", geometry = Rect(50.4, 7.0) };
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync($"/admin/cities/{cityId}/districts", body, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PutAsJsonAsync($"/admin/districts/{id}", body, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PutAsJsonAsync($"/admin/districts/{id}/geometry", body, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync($"/admin/cities/{cityId}/districts/validate", body, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", body, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.DeleteAsync($"/admin/districts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync($"/districts/{id}")).StatusCode);
    }

    // ---- import ------------------------------------------------------------------------------------------------

    private static JsonObject Feature(string? name, JsonNode geometry, string property = "NAME")
        => new() { ["type"] = "Feature", ["properties"] = new JsonObject { [property] = name, ["INFO"] = "info " + name }, ["geometry"] = geometry };

    private static JsonObject Collection(params JsonNode[] features) => new() { ["type"] = "FeatureCollection", ["features"] = new JsonArray(features) };

    [Fact]
    public async Task A_feature_collection_is_imported_as_districts()
    {
        var (admin, cityId) = await NewCityAsync();
        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", new
        {
            geoJson = Collection(Feature("Nordviertel", Rect(51.0, 7.0)), Feature("Südviertel", Rect(51.0, 7.01)), Feature("Ostviertel", Rect(51.01, 7.0))),
            nameProperty = "NAME", descriptionProperty = "INFO",
        }, Web);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await Body(res);
        Assert.Equal(3, body.GetProperty("created").GetInt32());
        Assert.Equal(new[] { "nordviertel", "ostviertel", "suedviertel" }, body.GetProperty("districts").EnumerateArray().Select(d => d.GetProperty("key").GetString()!).Order());

        var list = (await admin.GetFromJsonAsync<List<JsonElement>>($"/admin/cities/{cityId}/districts?geometry=true"))!;
        Assert.Equal(3, list.Count);
        Assert.All(list, d => Assert.Equal(4, d.GetProperty("pointCount").GetInt32()));
        Assert.Contains(list, d => d.GetProperty("description").GetString() == "info Nordviertel");
    }

    [Fact]
    public async Task An_import_with_a_problem_saves_nothing()
    {
        var (admin, cityId) = await NewCityAsync();
        await CreateDistrictAsync(admin, cityId, "Vorhanden", Rect(52.0, 7.0));
        var bowTie = Ring((52.5, 7.0), (52.51, 7.01), (52.5, 7.01), (52.51, 7.0));
        var withHole = JsonNode.Parse("""{"type":"Polygon","coordinates":[[[7,53],[7.1,53],[7.1,53.1],[7,53.1],[7,53]],[[7.02,53.02],[7.04,53.02],[7.04,53.04],[7.02,53.02]]]}""")!;

        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", new
        {
            geoJson = Collection(
                Feature("Gut", Rect(52.2, 7.0)),
                Feature("Schleife", bowTie),
                Feature("Loch", withHole),
                Feature("Vorhanden", Rect(52.3, 7.0)),          // name exists already
                Feature("Ueberlappt", Rect(52.0, 7.005)),        // overlaps the existing district
                Feature(null, Rect(52.4, 7.0)),                  // no name
                Feature("Gut", Rect(52.6, 7.0))),                // repeated name in the file
            nameProperty = "NAME",
        }, Web);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await Body(res);
        Assert.Equal("invalid_import", body.GetProperty("error").GetString());
        var features = body.GetProperty("details").GetProperty("features").EnumerateArray().ToDictionary(f => f.GetProperty("index").GetInt32());
        Assert.DoesNotContain(0, features.Keys); // the good one is not reported
        Assert.Contains("self_intersection", ProblemCodes(features[1]));
        Assert.Contains("holes_not_supported", ProblemCodes(features[2]));
        Assert.Contains("duplicate_name", ProblemCodes(features[3]));
        Assert.Contains("overlaps_district", ProblemCodes(features[4]));
        Assert.Contains("missing_name", ProblemCodes(features[5]));
        Assert.Contains("duplicate_name", ProblemCodes(features[6]));

        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/admin/cities/{cityId}/districts")).GetArrayLength()); // only the existing one
    }

    [Fact]
    public async Task Imported_districts_are_checked_against_each_other()
    {
        var (admin, cityId) = await NewCityAsync();
        var res = await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", new
        {
            geoJson = Collection(Feature("Eins", Rect(54.0, 7.0)), Feature("Zwei", Rect(54.0, 7.005))),
            nameProperty = "NAME",
        }, Web);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var features = (await Body(res)).GetProperty("details").GetProperty("features").EnumerateArray().ToList();
        Assert.Equal(1, features.Single().GetProperty("index").GetInt32());
        Assert.Contains("overlaps_district", ProblemCodes(features.Single()));
    }

    [Fact]
    public async Task Import_needs_a_feature_collection_and_a_name_property()
    {
        var (admin, cityId) = await NewCityAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", new { geoJson = Collection(Feature("X", Rect(55, 7))) }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", new { geoJson = Rect(55, 7), nameProperty = "NAME" }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/cities/{cityId}/districts/import", new { geoJson = Collection(), nameProperty = "NAME" }, Web)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/admin/cities/{Guid.NewGuid()}/districts/import", new { geoJson = Collection(Feature("X", Rect(55, 7))), nameProperty = "NAME" }, Web)).StatusCode);
    }

    // ---- points, leaderboard, deleting ---------------------------------------------------------------------------

    private async Task<Guid> ApprovedSubmissionAtAsync(HttpClient admin, HttpClient player, double lat, double lon, int reward)
    {
        var assetId = await api.AddTreeAsync(lat, lon);
        var questId = await api.CreateQuestAsync(admin, assetId, rewardPoints: reward);
        var claim = await Body(await player.PostAsync($"/quests/{questId}/claim", null));
        var form = new MultipartFormDataContent
        {
            { new StringContent(lat.ToString(CultureInfo.InvariantCulture)), "lat" },
            { new StringContent(lon.ToString(CultureInfo.InvariantCulture)), "lon" },
            { new StringContent(JsonSerializer.Serialize(new { value = "Tilia" })), "payload" },
        };
        var submitted = await player.PostAsync($"/claims/{claim.GetProperty("id").GetGuid()}/submit", form);
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var submissionId = (await Body(submitted)).GetProperty("submissionId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/submissions/{submissionId}/review", new { approved = true })).StatusCode);
        return submissionId;
    }

    private async Task WaitForLedgerAsync(Guid submissionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await api.WithDb(db => db.PointTransactions.AnyAsync(p => p.SubmissionId == submissionId))) return;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The points were not paid within 15 s.");
    }

    [Fact]
    public async Task Points_go_to_the_district_of_the_tree_and_the_leaderboard_ranks_the_districts()
    {
        var (admin, cityId) = await NewCityAsync();
        var north = await CreateDistrictAsync(admin, cityId, "Nord", Rect(56.0, 7.0));
        var south = await CreateDistrictAsync(admin, cityId, "Sued", Rect(56.0, 7.01));
        var empty = await CreateDistrictAsync(admin, cityId, "Leer", Rect(56.5, 7.0));
        var (anna, annaName, _) = await api.RegisterAsync("anna");
        var (ben, benName, _) = await api.RegisterAsync("ben");

        var s1 = await ApprovedSubmissionAtAsync(admin, anna, 56.005, 7.005, 30);   // north
        var s2 = await ApprovedSubmissionAtAsync(admin, ben, 56.004, 7.006, 20);    // north
        var s3 = await ApprovedSubmissionAtAsync(admin, anna, 56.005, 7.015, 40);   // south
        var s4 = await ApprovedSubmissionAtAsync(admin, ben, 57.5, 7.5, 99);        // in no district
        foreach (var s in new[] { s1, s2, s3, s4 }) await WaitForLedgerAsync(s);

        // ledger and cached totals
        var rows = await api.WithDb(db => db.PointTransactions.Where(p => new[] { s1, s2, s3, s4 }.Contains(p.SubmissionId!.Value)).ToListAsync());
        Assert.Equal(north, rows.Single(r => r.SubmissionId == s1).DistrictId);
        Assert.Equal(north, rows.Single(r => r.SubmissionId == s2).DistrictId);
        Assert.Equal(south, rows.Single(r => r.SubmissionId == s3).DistrictId);
        Assert.Null(rows.Single(r => r.SubmissionId == s4).DistrictId);
        var totals = await api.WithDb(db => db.Districts.Where(d => d.CityId == cityId).ToDictionaryAsync(d => d.Id, d => d.TotalPoints));
        Assert.Equal((50, 40, 0), (totals[north], totals[south], totals[empty]));

        // the ranking: north 50, south 40, the empty one last with 0
        var ranking = (await anna.GetFromJsonAsync<List<JsonElement>>($"/cities/{cityId}/leaderboard"))!;
        Assert.Equal(new[] { "Nord", "Sued", "Leer" }, ranking.Select(r => r.GetProperty("name").GetString()!));
        Assert.Equal(new[] { 1, 2, 3 }, ranking.Select(r => r.GetProperty("rank").GetInt32()));
        Assert.Equal(new[] { 50, 40, 0 }, ranking.Select(r => r.GetProperty("points").GetInt32()));
        Assert.Equal(new[] { 2, 1, 0 }, ranking.Select(r => r.GetProperty("contributors").GetInt32()));
        Assert.Equal(new[] { 2, 1, 0 }, ranking.Select(r => r.GetProperty("contributions").GetInt32()));

        var detail = await anna.GetFromJsonAsync<JsonElement>($"/districts/{north}");
        Assert.Equal((1, 2, 50), (detail.GetProperty("rank").GetInt32(), detail.GetProperty("contributors").GetInt32(), detail.GetProperty("totalPoints").GetInt32()));

        // top players of the north
        var top = (await anna.GetFromJsonAsync<List<JsonElement>>($"/districts/{north}/leaderboard"))!;
        Assert.Equal(new[] { annaName, benName }, top.Select(t => t.GetProperty("username").GetString()!));
        Assert.Equal(new[] { 30, 20 }, top.Select(t => t.GetProperty("points").GetInt32()));
        Assert.Single((await anna.GetFromJsonAsync<List<JsonElement>>($"/districts/{north}/leaderboard?limit=1"))!);
        Assert.Empty((await anna.GetFromJsonAsync<List<JsonElement>>($"/districts/{empty}/leaderboard"))!);

        Assert.Equal(HttpStatusCode.BadRequest, (await anna.GetAsync($"/cities/{cityId}/leaderboard?period=year")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync($"/cities/{Guid.NewGuid()}/leaderboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync($"/districts/{Guid.NewGuid()}/leaderboard")).StatusCode);
    }

    [Fact]
    public async Task Districts_with_equal_points_share_a_rank_and_the_week_only_counts_recent_points()
    {
        var (admin, cityId) = await NewCityAsync();
        var a = await CreateDistrictAsync(admin, cityId, "Alpha", Rect(58.0, 7.0));
        var b = await CreateDistrictAsync(admin, cityId, "Beta", Rect(58.0, 7.01));
        var (player, username, _) = await api.RegisterAsync("weekly");
        var userId = await api.WithDb(db => db.Users.Where(u => u.Username == username).Select(u => u.Id).FirstAsync());

        // Beta earned its points two weeks ago (the test clock says Friday 25 September 2026), Alpha just now
        await api.WithDb(async db =>
        {
            db.PointTransactions.Add(new PointTransaction { UserId = userId, DistrictId = a, Amount = 10, Reason = PointReason.Correction, CreatedAt = api.Clock.GetUtcNow().AddHours(-1) });
            db.PointTransactions.Add(new PointTransaction { UserId = userId, DistrictId = b, Amount = 10, Reason = PointReason.Correction, CreatedAt = api.Clock.GetUtcNow().AddDays(-14) });
            await db.SaveChangesAsync();
            return 0;
        });

        var all = (await player.GetFromJsonAsync<List<JsonElement>>($"/cities/{cityId}/leaderboard?period=all"))!;
        Assert.Equal(new[] { 1, 1 }, all.Select(r => r.GetProperty("rank").GetInt32())); // 10 points each, one contributor each
        Assert.Equal(new[] { "Alpha", "Beta" }, all.Select(r => r.GetProperty("name").GetString()!)); // ties by name

        var week = (await player.GetFromJsonAsync<List<JsonElement>>($"/cities/{cityId}/leaderboard?period=week"))!;
        Assert.Equal(new[] { "Alpha", "Beta" }, week.Select(r => r.GetProperty("name").GetString()!));
        Assert.Equal(new[] { 10, 0 }, week.Select(r => r.GetProperty("points").GetInt32()));
        Assert.Equal(new[] { 1, 2 }, week.Select(r => r.GetProperty("rank").GetInt32()));
        Assert.Empty((await player.GetFromJsonAsync<List<JsonElement>>($"/districts/{b}/leaderboard?period=week"))!);
        Assert.Single((await player.GetFromJsonAsync<List<JsonElement>>($"/districts/{b}/leaderboard?period=all"))!);
    }

    [Fact]
    public async Task Redrawing_a_district_does_not_change_the_points_already_awarded()
    {
        var (admin, cityId) = await NewCityAsync();
        var district = await CreateDistrictAsync(admin, cityId, "Wandernd", Rect(59.0, 7.0));
        var (player, _, _) = await api.RegisterAsync("stayer");
        var s = await ApprovedSubmissionAtAsync(admin, player, 59.005, 7.005, 15);
        await WaitForLedgerAsync(s);

        // move the district away: the tree no longer lies in it, the old points stay with it
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/admin/districts/{district}/geometry", new { geometry = Rect(59.5, 7.0) }, Web)).StatusCode);
        var line = await api.WithDb(db => db.PointTransactions.SingleAsync(p => p.SubmissionId == s));
        Assert.Equal(district, line.DistrictId);
        var ranking = (await player.GetFromJsonAsync<List<JsonElement>>($"/cities/{cityId}/leaderboard"))!;
        Assert.Equal(15, ranking.Single().GetProperty("points").GetInt32());

        // a new approval at the old spot finds no district any more
        var s2 = await ApprovedSubmissionAtAsync(admin, player, 59.006, 7.006, 5);
        await WaitForLedgerAsync(s2);
        Assert.Null((await api.WithDb(db => db.PointTransactions.SingleAsync(p => p.SubmissionId == s2))).DistrictId);
    }

    [Fact]
    public async Task A_deactivated_district_takes_no_new_points()
    {
        var (admin, cityId) = await NewCityAsync();
        var district = await CreateDistrictAsync(admin, cityId, "Ruhend", Rect(60.0, 7.0));
        await admin.PutAsJsonAsync($"/admin/districts/{district}", new { isActive = false }, Web);
        var (player, _, _) = await api.RegisterAsync("idle");
        var s = await ApprovedSubmissionAtAsync(admin, player, 60.005, 7.005, 12);
        await WaitForLedgerAsync(s);
        Assert.Null((await api.WithDb(db => db.PointTransactions.SingleAsync(p => p.SubmissionId == s))).DistrictId);
        Assert.Equal(0, await api.WithDb(db => db.Districts.Where(d => d.Id == district).Select(d => d.TotalPoints).SingleAsync()));
    }

    [Fact]
    public async Task Deleting_needs_an_empty_district_and_an_empty_city()
    {
        var (admin, cityId) = await NewCityAsync();
        var used = await CreateDistrictAsync(admin, cityId, "Benutzt", Rect(61.0, 7.0));
        var unused = await CreateDistrictAsync(admin, cityId, "Unbenutzt", Rect(61.5, 7.0));
        var (player, _, _) = await api.RegisterAsync("deleter");
        await WaitForLedgerAsync(await ApprovedSubmissionAtAsync(admin, player, 61.005, 7.005, 10));

        var refused = await admin.DeleteAsync($"/admin/districts/{used}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("district_has_points", (await Body(refused)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/districts/{unused}")).StatusCode);
        Assert.Equal(0, await api.WithDb(db => db.DistrictPoints.CountAsync(p => p.DistrictId == unused))); // its points went with it
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/admin/districts/{unused}")).StatusCode);

        var cityRefused = await admin.DeleteAsync($"/admin/cities/{cityId}");
        Assert.Equal(HttpStatusCode.Conflict, cityRefused.StatusCode);
        Assert.Equal("city_has_districts", (await Body(cityRefused)).GetProperty("error").GetString());
    }
}
