using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Api.Services;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Api.Districts;

/// <summary>Write side for cities (admin panel).</summary>
public interface ICityAdmin
{
    Task<ServiceResult<CityDto>> CreateAsync(CityRequest req, CancellationToken ct);
    Task<ServiceResult<CityDto>> UpdateAsync(Guid id, CityRequest req, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>Write side for districts (admin panel): draw, redraw, describe, deactivate, import.</summary>
public interface IDistrictAdmin
{
    Task<ServiceResult<DistrictDto>> CreateAsync(Guid cityId, Guid adminId, DistrictRequest req, CancellationToken ct);
    Task<ServiceResult<DistrictDto>> UpdateAsync(Guid id, DistrictRequest req, CancellationToken ct);
    Task<ServiceResult<DistrictDto>> ReplaceGeometryAsync(Guid id, GeometryRequest req, CancellationToken ct);
    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct);
    /// <summary>Dry run: what would be wrong with this outline. Saves nothing.</summary>
    Task<ServiceResult<GeometryCheckDto>> ValidateAsync(Guid cityId, GeometryRequest req, CancellationToken ct);
    Task<ServiceResult<ImportResultDto>> ImportAsync(Guid cityId, Guid adminId, DistrictImportRequest req, CancellationToken ct);
}

public sealed partial class CityAdmin(AppDbContext db, TimeProvider clock) : ICityAdmin
{
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex KeyPattern();

    public async Task<ServiceResult<CityDto>> CreateAsync(CityRequest req, CancellationToken ct)
    {
        var errors = Validate(req, creating: true);
        var name = req.Name?.Trim() ?? "";
        var key = string.IsNullOrWhiteSpace(req.Key) ? Slug.From(name) : req.Key.Trim().ToLowerInvariant();
        if (key.Length is 0 or > 64 || !KeyPattern().IsMatch(key)) errors["key"] = ["Use lower-case letters, digits and single hyphens (max 64 characters)."];
        if (errors.Count > 0) return Invalid<CityDto>(errors);
        if (await db.Cities.AnyAsync(c => c.Key == key, ct)) return ServiceResult<CityDto>.Fail(409, "key_taken", $"A city with the key '{key}' exists.");

        var now = clock.GetUtcNow();
        var city = new City { Key = key, Name = name, CreatedAt = now, UpdatedAt = now };
        Apply(city, req);
        db.Cities.Add(city);
        await db.SaveChangesAsync(ct);
        return ServiceResult<CityDto>.Success(DistrictDirectory.ToDto(city, 0));
    }

    public async Task<ServiceResult<CityDto>> UpdateAsync(Guid id, CityRequest req, CancellationToken ct)
    {
        var city = await db.Cities.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (city is null) return ServiceResult<CityDto>.Fail(404, "city_not_found");
        var errors = Validate(req, creating: false);
        if (errors.Count > 0) return Invalid<CityDto>(errors);

        if (req.Name is not null) city.Name = req.Name.Trim();
        Apply(city, req);
        city.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var count = await db.Districts.CountAsync(d => d.CityId == id && d.IsActive, ct);
        return ServiceResult<CityDto>.Success(DistrictDirectory.ToDto(city, count));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var city = await db.Cities.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (city is null) return ServiceResult<bool>.Fail(404, "city_not_found");
        if (await db.Districts.AnyAsync(d => d.CityId == id, ct))
            return ServiceResult<bool>.Fail(409, "city_has_districts", "Delete or move the city's districts first, or deactivate the city instead.");
        db.Cities.Remove(city);
        await db.SaveChangesAsync(ct);
        return ServiceResult<bool>.Success(true);
    }

    private static void Apply(City city, CityRequest req)
    {
        if (req.CountryCode is not null) city.CountryCode = string.IsNullOrWhiteSpace(req.CountryCode) ? null : req.CountryCode.Trim().ToUpperInvariant();
        if (req.CenterLat is not null || req.CenterLon is not null) { city.CenterLat = req.CenterLat; city.CenterLon = req.CenterLon; }
        if (req.DefaultZoom is not null) city.DefaultZoom = req.DefaultZoom;
        if (!string.IsNullOrWhiteSpace(req.Timezone)) city.Timezone = req.Timezone.Trim();
        if (req.IsActive is not null) city.IsActive = req.IsActive.Value;
    }

    private static Dictionary<string, string[]> Validate(CityRequest req, bool creating)
    {
        var errors = new Dictionary<string, string[]>();
        if (creating && string.IsNullOrWhiteSpace(req.Name)) errors["name"] = ["A name is required."];
        else if (req.Name is not null && (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 128)) errors["name"] = ["1 to 128 characters."];
        if (req.CountryCode is { Length: > 0 } cc && !Regex.IsMatch(cc.Trim(), "^[A-Za-z]{2}$")) errors["countryCode"] = ["Two letters (ISO 3166-1), e.g. DE."];
        if ((req.CenterLat is null) != (req.CenterLon is null)) errors["center"] = ["Give centerLat and centerLon together."];
        if (req.CenterLat is < -90 or > 90) errors["centerLat"] = ["Between -90 and 90."];
        if (req.CenterLon is < -180 or > 180) errors["centerLon"] = ["Between -180 and 180."];
        if (req.DefaultZoom is < 1 or > 22) errors["defaultZoom"] = ["Between 1 and 22."];
        if (!string.IsNullOrWhiteSpace(req.Timezone) && !TimeZoneInfo.TryFindSystemTimeZoneById(req.Timezone.Trim(), out _))
            errors["timezone"] = ["Unknown time zone, use an IANA id such as Europe/Berlin."];
        return errors;
    }

    internal static ServiceResult<T> Invalid<T>(Dictionary<string, string[]> errors)
        => ServiceResult<T>.Fail(400, "validation_failed", "The request is not valid.", errors);
}

public sealed partial class DistrictAdmin(AppDbContext db, IDistrictShapeChecker checker, TimeProvider clock) : IDistrictAdmin
{
    private const int MaxImportFeatures = 500;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")] private static partial Regex KeyPattern();
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex ColorPattern();

    public async Task<ServiceResult<DistrictDto>> CreateAsync(Guid cityId, Guid adminId, DistrictRequest req, CancellationToken ct)
    {
        if (!await db.Cities.AnyAsync(c => c.Id == cityId, ct)) return ServiceResult<DistrictDto>.Fail(404, "city_not_found");

        var name = req.Name?.Trim() ?? "";
        var key = string.IsNullOrWhiteSpace(req.Key) ? Slug.From(name) : req.Key.Trim().ToLowerInvariant();
        var errors = ValidateFields(name, key, req.Description, req.Color, requireName: true);
        if (errors.Count > 0) return CityAdmin.Invalid<DistrictDto>(errors);
        if (await NameOrKeyTakenAsync(cityId, name, key, null, ct) is { } taken) return ServiceResult<DistrictDto>.Fail(409, taken.Code, taken.Message);

        if (!GeoJsonPolygons.TryRead(req.Geometry, out var points, out var readProblem)) return InvalidGeometry<DistrictDto>([readProblem!]);
        var check = await checker.CheckAsync(cityId, null, points, null, ct);
        if (!check.Valid) return InvalidGeometry<DistrictDto>(check.Problems);

        var now = clock.GetUtcNow();
        var district = new District
        {
            CityId = cityId, Key = key, Name = name, Description = Clean(req.Description), Color = Clean(req.Color)?.ToUpperInvariant(),
            IsActive = req.IsActive ?? true, CreatedBy = adminId, CreatedAt = now, UpdatedAt = now,
        };
        SetShape(district, check);
        db.Districts.Add(district);
        db.DistrictPoints.AddRange(Points(district.Id, check.Ring));
        await db.SaveChangesAsync(ct);
        return ServiceResult<DistrictDto>.Success(DistrictDirectory.ToDto(district, check.Ring, includeGeometry: true));
    }

    public async Task<ServiceResult<DistrictDto>> UpdateAsync(Guid id, DistrictRequest req, CancellationToken ct)
    {
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null) return ServiceResult<DistrictDto>.Fail(404, "district_not_found");

        var name = req.Name is null ? district.Name : req.Name.Trim();
        var errors = ValidateFields(name, district.Key, req.Description ?? district.Description, req.Color ?? district.Color, requireName: true);
        if (errors.Count > 0) return CityAdmin.Invalid<DistrictDto>(errors);
        if (req.Name is not null && await NameOrKeyTakenAsync(district.CityId, name, null, id, ct) is { } taken)
            return ServiceResult<DistrictDto>.Fail(409, taken.Code, taken.Message);

        var ring = await LoadRingAsync(id, ct);
        if (req.IsActive == true && !district.IsActive)
        {
            // A district that comes back must not overlap what was drawn in the meantime.
            var check = await checker.CheckAsync(district.CityId, id, ring, null, ct);
            if (!check.Valid) return InvalidGeometry<DistrictDto>(check.Problems);
        }

        district.Name = name;
        if (req.Description is not null) district.Description = Clean(req.Description);
        if (req.Color is not null) district.Color = Clean(req.Color)?.ToUpperInvariant();
        if (req.IsActive is not null) district.IsActive = req.IsActive.Value;
        district.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ServiceResult<DistrictDto>.Success(DistrictDirectory.ToDto(district, ring, includeGeometry: true));
    }

    public async Task<ServiceResult<DistrictDto>> ReplaceGeometryAsync(Guid id, GeometryRequest req, CancellationToken ct)
    {
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null) return ServiceResult<DistrictDto>.Fail(404, "district_not_found");
        if (!GeoJsonPolygons.TryRead(req.Geometry, out var points, out var readProblem)) return InvalidGeometry<DistrictDto>([readProblem!]);
        var check = await checker.CheckAsync(district.CityId, id, points, null, ct);
        if (!check.Valid) return InvalidGeometry<DistrictDto>(check.Problems);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.DistrictPoints.Where(p => p.DistrictId == id).ExecuteDeleteAsync(ct);
        SetShape(district, check);
        district.UpdatedAt = clock.GetUtcNow();
        db.DistrictPoints.AddRange(Points(id, check.Ring));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ServiceResult<DistrictDto>.Success(DistrictDirectory.ToDto(district, check.Ring, includeGeometry: true));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null) return ServiceResult<bool>.Fail(404, "district_not_found");
        if (await db.PointTransactions.AnyAsync(p => p.DistrictId == id, ct))
            return ServiceResult<bool>.Fail(409, "district_has_points", "Points were earned in this district. Deactivate it instead (isActive = false) to keep the leaderboard history.");
        db.Districts.Remove(district);
        await db.SaveChangesAsync(ct);
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<GeometryCheckDto>> ValidateAsync(Guid cityId, GeometryRequest req, CancellationToken ct)
    {
        if (!await db.Cities.AnyAsync(c => c.Id == cityId, ct)) return ServiceResult<GeometryCheckDto>.Fail(404, "city_not_found");
        if (!GeoJsonPolygons.TryRead(req.Geometry, out var points, out var readProblem))
            return ServiceResult<GeometryCheckDto>.Success(new GeometryCheckDto(false, 0, null, null, [readProblem!]));
        var check = await checker.CheckAsync(cityId, req.DistrictId, points, null, ct);
        return ServiceResult<GeometryCheckDto>.Success(new GeometryCheckDto(
            check.Valid, check.Ring.Count, check.Valid ? check.Centroid.Lat : null, check.Valid ? check.Centroid.Lon : null, check.Problems));
    }

    public async Task<ServiceResult<ImportResultDto>> ImportAsync(Guid cityId, Guid adminId, DistrictImportRequest req, CancellationToken ct)
    {
        if (!await db.Cities.AnyAsync(c => c.Id == cityId, ct)) return ServiceResult<ImportResultDto>.Fail(404, "city_not_found");
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(req.NameProperty)) errors["nameProperty"] = ["Name of the property that holds the district name."];
        if (req.GeoJson is not JsonObject collection || collection["type"]?.GetValue<string>() != "FeatureCollection" || collection["features"] is not JsonArray features)
            errors["geoJson"] = ["A GeoJSON FeatureCollection is required."];
        if (errors.Count > 0) return CityAdmin.Invalid<ImportResultDto>(errors);
        var featureArray = (JsonArray)((JsonObject)req.GeoJson!)["features"]!;
        if (featureArray.Count is 0 or > MaxImportFeatures)
            return CityAdmin.Invalid<ImportResultDto>(new() { ["geoJson"] = [$"1 to {MaxImportFeatures} features."] });

        var existing = await db.Districts.AsNoTracking().Where(d => d.CityId == cityId).Select(d => new { d.Name, d.Key }).ToListAsync(ct);
        var names = existing.Select(d => d.Name.ToLowerInvariant()).ToHashSet();
        var keys = existing.Select(d => d.Key).ToHashSet();

        var now = clock.GetUtcNow();
        var pending = new List<PendingShape>();
        var created = new List<(District District, ShapeCheck Check)>();
        var failures = new List<object>();

        for (var i = 0; i < featureArray.Count; i++)
        {
            var feature = featureArray[i] as JsonObject;
            var props = feature?["properties"] as JsonObject;
            var name = PropertyText(props, req.NameProperty!);
            var problems = new List<GeometryProblemDto>();
            if (string.IsNullOrWhiteSpace(name)) problems.Add(new("missing_name", $"Feature {i} has no '{req.NameProperty}' property."));
            var key = PropertyText(props, req.KeyProperty) is { Length: > 0 } k ? k.ToLowerInvariant() : Slug.From(name ?? "");
            if (name is not null)
            {
                foreach (var e in ValidateFields(name, key, PropertyText(props, req.DescriptionProperty), null, requireName: true))
                    problems.Add(new("invalid_field", $"{e.Key}: {e.Value[0]}"));
                if (!names.Add(name.ToLowerInvariant())) problems.Add(new("duplicate_name", $"A district named '{name}' already exists in the city or the file."));
                if (!keys.Add(key)) problems.Add(new("duplicate_key", $"The key '{key}' already exists in the city or the file."));
            }

            ShapeCheck? check = null;
            if (!GeoJsonPolygons.TryRead(feature?["geometry"], out var points, out var readProblem)) problems.Add(readProblem!);
            else
            {
                check = await checker.CheckAsync(cityId, null, points, pending, ct);
                problems.AddRange(check.Problems);
            }

            if (problems.Count > 0) { failures.Add(new { index = i, name, problems }); continue; }
            pending.Add(new PendingShape(name!, check!.Polygon!));
            var district = new District
            {
                CityId = cityId, Key = key, Name = name!.Trim(), Description = Clean(PropertyText(props, req.DescriptionProperty)),
                CreatedBy = adminId, CreatedAt = now, UpdatedAt = now,
            };
            SetShape(district, check);
            created.Add((district, check));
        }

        if (failures.Count > 0)
            return ServiceResult<ImportResultDto>.Fail(422, "invalid_import", "Nothing was imported: fix the features listed in details.", new { features = failures });

        foreach (var (district, check) in created)
        {
            db.Districts.Add(district);
            db.DistrictPoints.AddRange(Points(district.Id, check.Ring));
        }
        await db.SaveChangesAsync(ct); // one save = all or nothing
        return ServiceResult<ImportResultDto>.Success(new ImportResultDto(
            created.Count, created.Select(c => DistrictDirectory.ToDto(c.District, c.Check.Ring, includeGeometry: false)).ToList()));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static void SetShape(District district, ShapeCheck check)
    {
        district.Geom = check.Polygon!;
        district.CentroidLat = check.Centroid.Lat;
        district.CentroidLon = check.Centroid.Lon;
    }

    private static IEnumerable<DistrictPoint> Points(Guid districtId, IReadOnlyList<GeoPoint> ring)
        => ring.Select((p, i) => new DistrictPoint { DistrictId = districtId, Position = i, Lat = p.Lat, Lon = p.Lon });

    private async Task<List<GeoPoint>> LoadRingAsync(Guid districtId, CancellationToken ct)
        => (await db.DistrictPoints.AsNoTracking().Where(p => p.DistrictId == districtId).OrderBy(p => p.Position).ToListAsync(ct))
            .Select(p => new GeoPoint(p.Lat, p.Lon)).ToList();

    private async Task<(string Code, string Message)?> NameOrKeyTakenAsync(Guid cityId, string name, string? key, Guid? exceptId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        if (await db.Districts.AnyAsync(d => d.CityId == cityId && d.Id != exceptId && d.Name.ToLower() == lowered, ct))
            return ("name_taken", $"A district named '{name}' exists in this city.");
        if (key is not null && await db.Districts.AnyAsync(d => d.CityId == cityId && d.Id != exceptId && d.Key == key, ct))
            return ("key_taken", $"A district with the key '{key}' exists in this city.");
        return null;
    }

    private static Dictionary<string, string[]> ValidateFields(string name, string key, string? description, string? color, bool requireName)
    {
        var errors = new Dictionary<string, string[]>();
        if (requireName && (name.Length == 0 || name.Length > 128)) errors["name"] = ["1 to 128 characters."];
        if (key.Length is 0 or > 64 || !KeyPattern().IsMatch(key)) errors["key"] = ["Use lower-case letters, digits and single hyphens (max 64 characters)."];
        if (description is { Length: > 2000 }) errors["description"] = ["At most 2000 characters."];
        if (!string.IsNullOrEmpty(color) && !ColorPattern().IsMatch(color)) errors["color"] = ["A colour like #2C8054."];
        return errors;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? PropertyText(JsonObject? props, string? name)
        => name is not null && props?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim()
         : name is not null && props?[name] is JsonValue n ? n.ToString() : null;

    private static ServiceResult<T> InvalidGeometry<T>(IReadOnlyList<GeometryProblemDto> problems)
        => ServiceResult<T>.Fail(422, "invalid_geometry", "The outline is not valid. See details.problems.", new { problems });
}
