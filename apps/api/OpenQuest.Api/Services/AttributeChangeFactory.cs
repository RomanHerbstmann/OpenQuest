using System.Text.Json.Nodes;
using OpenQuest.Api.Data;
using OpenQuest.Core.Domain;

namespace OpenQuest.Api.Services;

/// <summary>Derives the attribute change a submission proposes (which attribute, old value, new value).</summary>
public interface IAttributeChangeFactory
{
    AttributeChange? Propose(Quest quest, TaskType type, JsonObject? payload, Media? media, Guid submissionId);
}

public sealed class AttributeChangeFactory : IAttributeChangeFactory
{
    public AttributeChange? Propose(Quest quest, TaskType type, JsonObject? payload, Media? media, Guid submissionId)
    {
        var config = JsonUtil.ParseObject(quest.TaskConfig);
        var key = TaskTypes.ChangedAttribute(type, config?["attribute"]?.GetValue<string>());
        if (key is null) return null;

        JsonNode? newValue = type switch
        {
            TaskType.Photo => media is null ? null : JsonValue.Create($"/media/{media.Id}"),
            TaskType.ConditionReport => payload?["condition"]?.DeepClone(),
            _ => payload?["value"]?.DeepClone(),
        };
        if (newValue is null) return null;

        var attrs = JsonUtil.ParseObject(quest.Asset.Attributes);
        return new AttributeChange
        {
            SubmissionId = submissionId,
            AssetId = quest.AssetId,
            AttributeKey = key,
            OldValue = attrs?[key]?.ToJsonString(),
            NewValue = newValue.ToJsonString(),
            Status = ChangeStatus.Proposed,
        };
    }
}
