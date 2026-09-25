using System.Text.Json;

namespace OpenQuest.Core.Domain;

/// <summary>What the player has to do on site. Stored as snake_case key (see <see cref="TaskTypes"/>).</summary>
public enum TaskType
{
    Photo,
    VerifyAttribute,
    Measure,
    ConditionReport,
}

public sealed record TaskTypeDefinition(TaskType Type, string Name, string ConfigSchemaJson, string ResultSchemaJson)
{
    public string Key => Type.Key();
}

public static class TaskTypes
{
    public static string Key(this TaskType type) => JsonNamingPolicy.SnakeCaseLower.ConvertName(type.ToString());

    public static bool TryParse(string? key, out TaskType type)
    {
        foreach (var t in Enum.GetValues<TaskType>())
            if (string.Equals(t.Key(), key, StringComparison.OrdinalIgnoreCase)) { type = t; return true; }
        type = default;
        return false;
    }

    /// <summary>
    /// Attribute of the asset that an accepted submission of this task type changes.
    /// <paramref name="taskConfigAttribute"/> is <c>quest.task_config.attribute</c> where relevant.
    /// </summary>
    public static string? ChangedAttribute(TaskType type, string? taskConfigAttribute) => type switch
    {
        TaskType.Photo => "photo_url",
        TaskType.ConditionReport => "condition",
        TaskType.VerifyAttribute or TaskType.Measure => taskConfigAttribute,
        _ => null,
    };

    private const string EmptyObject = """{"type":"object","additionalProperties":false,"properties":{}}""";

    private const string AttributeConfig = """
        {"type":"object","required":["attribute"],"additionalProperties":false,
         "properties":{"attribute":{"type":"string","minLength":1,"maxLength":64},"unit":{"type":"string","maxLength":16}}}
        """;

    public static readonly IReadOnlyList<TaskTypeDefinition> All =
    [
        new(TaskType.Photo, "task_type.photo", EmptyObject,
            """{"type":"object","additionalProperties":false,"properties":{"note":{"type":"string","maxLength":500}}}"""),
        new(TaskType.VerifyAttribute, "task_type.verify_attribute", AttributeConfig,
            """
            {"type":"object","required":["value"],"additionalProperties":false,
             "properties":{"value":{"type":"string","minLength":1,"maxLength":200},"confirmed":{"type":"boolean"},"note":{"type":"string","maxLength":500}}}
            """),
        new(TaskType.Measure, "task_type.measure", AttributeConfig,
            """
            {"type":"object","required":["value"],"additionalProperties":false,
             "properties":{"value":{"type":"number","exclusiveMinimum":0},"note":{"type":"string","maxLength":500}}}
            """),
        new(TaskType.ConditionReport, "task_type.condition_report", EmptyObject,
            """
            {"type":"object","required":["condition"],"additionalProperties":false,
             "properties":{"condition":{"enum":["good","damaged","dead","gone"]},"note":{"type":"string","maxLength":500}}}
            """),
    ];
}
