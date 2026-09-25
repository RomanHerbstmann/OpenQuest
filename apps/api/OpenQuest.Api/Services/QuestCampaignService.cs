using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using OpenQuest.Api.Config;
using OpenQuest.Api.Contracts;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Services;

/// <summary>Creates quest campaigns: one quest per asset selected by a target (ids, map area, attribute filter).</summary>
public interface IQuestCampaignService
{
    Task<ServiceResult<CreateQuestsResult>> CreateAsync(Guid adminId, CreateQuestsRequest request, CancellationToken ct);
}

public sealed class QuestCampaignService(
    AppDbContext db, IJsonSchemaValidator schemas, IOptions<GameOptions> game, TimeProvider clock) : IQuestCampaignService
{
    private const int MaxQuestsPerRequest = 5000;
    private static readonly GeometryFactory Wgs84 = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public async Task<ServiceResult<CreateQuestsResult>> CreateAsync(Guid adminId, CreateQuestsRequest req, CancellationToken ct)
    {
        var target = req.Target ?? new QuestTarget(null, null, null, null, null, null);
        var errors = Validate(req, target, out var taskTypeEnum, out var assetTypeDef);
        if (errors.Count > 0) return Invalid(errors);

        var taskType = await db.TaskTypes.AsNoTracking().FirstAsync(x => x.Key == taskTypeEnum.Key(), ct);
        var assetType = await db.AssetTypes.AsNoTracking().FirstAsync(x => x.Key == assetTypeDef.Key, ct);

        var config = req.TaskConfig ?? new JsonObject();
        var configProblems = schemas.Validate(taskType.ConfigSchema, config);
        if (config["attribute"]?.GetValue<string>() is { } attr && !schemas.DeclaredProperties(assetType.AttributeSchema).Contains(attr))
            configProblems.Add($"attribute '{attr}' is not an attribute of asset type '{assetType.Key}'.");
        if (configProblems.Count > 0) return Invalid(new Dictionary<string, string[]> { ["taskConfig"] = [.. configProblems] });
        var configJson = config.ToJsonString();

        var assetIds = await SelectAssetsAsync(target, assetType.Id, taskType.Id, configJson, ct);
        if (assetIds.Count == 0) return ServiceResult<CreateQuestsResult>.Success(new CreateQuestsResult(0, null, []));

        var now = clock.GetUtcNow();
        var title = string.IsNullOrWhiteSpace(req.Title) ? DefaultTitle(taskTypeEnum, config["attribute"]?.GetValue<string>()) : req.Title.Trim();
        var campaign = new QuestCampaign
        {
            CreatedBy = adminId, Title = title, Description = req.Description?.Trim(), CreatedAt = now,
            AssetFilter = JsonSerializer.Serialize(target, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        db.QuestCampaigns.Add(campaign);

        var quests = assetIds.Select(assetId => new Quest
        {
            CampaignId = campaign.Id, AssetId = assetId, TaskTypeId = taskType.Id, CreatedBy = adminId,
            Title = title, Description = req.Description?.Trim(), TaskConfig = configJson,
            MaxCompletions = req.MaxCompletions, RewardPoints = req.RewardPoints,
            GeofenceRadiusM = req.GeofenceRadiusM ?? game.Value.GeofenceMeters,
            ClaimTtlMinutes = req.ClaimTtlMinutes ?? game.Value.ClaimTimeoutMinutes,
            Status = req.Status ?? QuestStatus.Active, StartsAt = req.StartsAt, EndsAt = req.EndsAt, CreatedAt = now,
        }).ToList();
        db.Quests.AddRange(quests);
        await db.SaveChangesAsync(ct);

        return ServiceResult<CreateQuestsResult>.Success(
            new CreateQuestsResult(quests.Count, campaign.Id, quests.Count <= 200 ? quests.Select(q => q.Id).ToList() : null));
    }

    private Dictionary<string, string[]> Validate(CreateQuestsRequest req, QuestTarget t, out TaskType taskType, out AssetType assetType)
    {
        var errors = new Dictionary<string, string[]>();
        var taskOk = TaskTypes.TryParse(req.TaskType, out taskType);
        if (!taskOk) errors["taskType"] = ["Unknown task type."];
        var assetOk = AssetType.TryParse(t.AssetType ?? "tree", out assetType);
        if (!assetOk) errors["target.assetType"] = ["Unknown asset type."];
        else if (taskOk && !assetType.AllowedTaskTypes.Contains(taskType)) errors["taskType"] = [$"Not allowed for asset type '{assetType.Key}'."];

        if (req.MaxCompletions is < 1 or > 1000) errors["maxCompletions"] = ["Must be between 1 and 1000."];
        if (req.RewardPoints is < 0 or > 100_000) errors["rewardPoints"] = ["Must be between 0 and 100000."];
        if (req.GeofenceRadiusM is < 5 or > 500) errors["geofenceRadiusM"] = ["Must be between 5 and 500."];
        if (req.ClaimTtlMinutes is < 1 or > 1440) errors["claimTtlMinutes"] = ["Must be between 1 and 1440."];
        if (req.EndsAt is { } end && req.StartsAt is { } start && end <= start) errors["endsAt"] = ["Must be after startsAt."];
        if (req.Status is not (null or QuestStatus.Active or QuestStatus.Draft)) errors["status"] = ["Only draft or active when creating."];
        if (t.AssetIds is not { Count: > 0 } && t.BBox is null && t.AttributeFilter is null && t.WithoutApprovedPhoto != true)
            errors["target"] = ["Select assets with assetIds, bbox, attributeFilter or withoutApprovedPhoto."];
        if (t.BBox is { } b && (b.MinLon >= b.MaxLon || b.MinLat >= b.MaxLat)) errors["target.bbox"] = ["Invalid bounding box."];
        return errors;
    }

    private async Task<List<Guid>> SelectAssetsAsync(QuestTarget t, Guid assetTypeId, Guid taskTypeId, string configJson, CancellationToken ct)
    {
        var limit = Math.Clamp(t.Limit ?? MaxQuestsPerRequest, 1, MaxQuestsPerRequest);
        var query = db.Assets.AsNoTracking().Where(a => a.Status == AssetStatus.Active && a.AssetTypeId == assetTypeId);
        if (t.AssetIds is { Count: > 0 } ids) query = query.Where(a => ids.Contains(a.Id));
        if (t.BBox is { } bb)
        {
            var poly = Wgs84.CreatePolygon([
                new Coordinate(bb.MinLon, bb.MinLat), new Coordinate(bb.MaxLon, bb.MinLat),
                new Coordinate(bb.MaxLon, bb.MaxLat), new Coordinate(bb.MinLon, bb.MaxLat),
                new Coordinate(bb.MinLon, bb.MinLat)]);
            query = query.Where(a => a.Geom.Intersects(poly));
        }
        if (t.AttributeFilter is { } filter)
        {
            var filterJson = filter.ToJsonString();
            query = query.Where(a => EF.Functions.JsonContains(a.Attributes, filterJson));
        }
        if (t.WithoutApprovedPhoto == true)
            query = query.Where(a => !db.AttributeChanges.Any(c => c.AssetId == a.Id && c.AttributeKey == "photo_url"
                                                                   && (c.Status == ChangeStatus.Accepted || c.Status == ChangeStatus.Exported)));

        // Don't create the same quest twice for an asset.
        var openStatuses = new[] { QuestStatus.Draft, QuestStatus.Active, QuestStatus.Paused, QuestStatus.Full };
        query = query.Where(a => !db.Quests.Any(q => q.AssetId == a.Id && q.TaskTypeId == taskTypeId
                                                    && openStatuses.Contains(q.Status)
                                                    && EF.Functions.JsonContains(q.TaskConfig, configJson)));
        return await query.Select(a => a.Id).Take(limit).ToListAsync(ct);
    }

    private static ServiceResult<CreateQuestsResult> Invalid(Dictionary<string, string[]> errors)
        => ServiceResult<CreateQuestsResult>.Fail(400, "validation_failed", "The request is not valid.", errors);

    private static string DefaultTitle(TaskType type, string? attribute) => type switch
    {
        TaskType.Photo => "Take a photo",
        TaskType.VerifyAttribute => string.IsNullOrEmpty(attribute) ? "Verify the data" : $"Verify: {attribute}",
        TaskType.Measure => string.IsNullOrEmpty(attribute) ? "Measure" : $"Measure: {attribute}",
        TaskType.ConditionReport => "Report the condition",
        _ => "Quest",
    };
}
