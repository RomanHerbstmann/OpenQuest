using System.Text.Json;
using OpenQuest.Core.Domain;

namespace OpenQuest.Core.Tests;

public class TaskTypesTests
{
    private static TaskTypeDefinition Definition(TaskType type) => TaskTypes.All.Single(t => t.Type == type);

    [Fact]
    public void Every_task_type_has_a_definition_with_valid_json_schemas()
    {
        foreach (var type in Enum.GetValues<TaskType>())
        {
            var d = Definition(type);
            JsonDocument.Parse(d.ConfigSchemaJson).Dispose();
            JsonDocument.Parse(d.ResultSchemaJson).Dispose();
        }
    }

    [Fact]
    public void The_condition_report_lists_exactly_the_known_issues()
    {
        var items = JsonDocument.Parse(Definition(TaskType.ConditionReport).ResultSchemaJson).RootElement
            .GetProperty("properties").GetProperty("issues").GetProperty("items").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(TaskTypes.IssueCodes, items);
        Assert.Contains("root_lift", items);
    }

    [Fact]
    public void Reporting_a_new_tree_changes_no_attribute_and_needs_a_data_source()
    {
        Assert.Null(TaskTypes.ChangedAttribute(TaskType.ReportNewTree, null));
        Assert.Contains("dataSource", JsonDocument.Parse(Definition(TaskType.ReportNewTree).ConfigSchemaJson).RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("report_new_tree", TaskType.ReportNewTree.Key());
        Assert.True(TaskTypes.TryParse("report_new_tree", out var parsed) && parsed == TaskType.ReportNewTree);
    }

    [Fact]
    public void Only_trees_can_be_reported_as_new()
    {
        Assert.Contains(TaskType.ReportNewTree, AssetType.Tree.AllowedTaskTypes);
        Assert.DoesNotContain(TaskType.ReportNewTree, AssetType.NaturalMonument.AllowedTaskTypes);
    }

    [Fact]
    public void Tree_and_monument_know_the_issues_attribute()
    {
        foreach (var type in AssetType.Known)
            Assert.True(JsonDocument.Parse(type.AttributeSchemaJson).RootElement.GetProperty("properties").TryGetProperty("issues", out _), type.Key);
    }
}
