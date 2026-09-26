using System.Text.Json.Nodes;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Services;

/// <summary>Derives the attribute changes a submission proposes (which attribute, old value, new value).</summary>
public interface IAttributeChangeFactory
{
    /// <summary>Usually one change; a condition report may add the reported <c>issues</c> as a second one. None for a quest without asset.</summary>
    IReadOnlyList<AttributeChange> Propose(Quest quest, TaskType type, JsonObject? payload, Media? media, Guid submissionId);
}

public sealed class AttributeChangeFactory : IAttributeChangeFactory
{
    public IReadOnlyList<AttributeChange> Propose(Quest quest, TaskType type, JsonObject? payload, Media? media, Guid submissionId)
    {
        if (quest.Asset is null || quest.AssetId is not { } assetId) return [];
        var attrs = JsonUtil.ParseObject(quest.Asset.Attributes);
        var config = JsonUtil.ParseObject(quest.TaskConfig);
        var changes = new List<AttributeChange>();

        void Add(string? key, JsonNode? newValue)
        {
            if (key is null || newValue is null) return;
            changes.Add(new AttributeChange
            {
                SubmissionId = submissionId, AssetId = assetId, AttributeKey = key,
                OldValue = attrs?[key]?.ToJsonString(), NewValue = newValue.ToJsonString(), Status = ChangeStatus.Proposed,
            });
        }

        var key = TaskTypes.ChangedAttribute(type, config?["attribute"]?.GetValue<string>());
        Add(key, type switch
        {
            TaskType.Photo => media is null ? null : JsonValue.Create($"/media/{media.Id}"),
            TaskType.ConditionReport => payload?["condition"]?.DeepClone(),
            _ => payload?["value"]?.DeepClone(),
        });
        if (type == TaskType.ConditionReport && payload?["issues"] is JsonArray { Count: > 0 } issues) Add("issues", issues.DeepClone());
        return changes;
    }
}
