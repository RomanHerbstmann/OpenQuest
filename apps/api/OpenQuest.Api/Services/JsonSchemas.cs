using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace OpenQuest.Api.Services;

/// <summary>Validates JSON against the JSON Schemas stored in asset_type / task_type rows.</summary>
public interface IJsonSchemaValidator
{
    /// <summary>Returns human-readable problems; empty if valid.</summary>
    List<string> Validate(string schemaJson, JsonNode? instance);

    /// <summary>Property names declared in a schema's top-level "properties".</summary>
    HashSet<string> DeclaredProperties(string schemaJson);
}

public sealed class JsonSchemaValidator : IJsonSchemaValidator
{
    private readonly ConcurrentDictionary<string, JsonSchema> _cache = new();

    public List<string> Validate(string schemaJson, JsonNode? instance)
    {
        var schema = _cache.GetOrAdd(schemaJson, s => JsonSchema.FromText(s));
        using var doc = JsonDocument.Parse(instance?.ToJsonString() ?? "null");
        var result = schema.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var problems = new List<string>();
        if (result.IsValid) return problems;
        foreach (var d in result.Details ?? [])
            if (d.Errors is { Count: > 0 })
                foreach (var (keyword, message) in d.Errors)
                    problems.Add($"{(d.InstanceLocation.ToString() is { Length: > 0 } loc ? loc : "/")}: {keyword}: {message}");
        if (problems.Count == 0) problems.Add("Does not match the schema.");
        return problems;
    }

    public HashSet<string> DeclaredProperties(string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        return doc.RootElement.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().Select(x => x.Name).ToHashSet()
            : [];
    }
}

public static class JsonUtil
{
    /// <summary>Parses a JSON object, or returns null if the text is not a JSON object.</summary>
    public static JsonObject? ParseObject(string? json)
    {
        try { return JsonNode.Parse(json ?? "") as JsonObject; }
        catch (JsonException) { return null; }
    }
}
