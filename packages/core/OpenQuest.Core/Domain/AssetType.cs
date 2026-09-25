namespace OpenQuest.Core.Domain;

/// <summary>
/// Kind of asset. Defines the JSON Schema of its attributes and which task types make sense for it.
/// New asset types need no DB migration: add a definition here, seed it, support it in an adapter.
/// </summary>
public sealed record AssetType(
    string Key, string Name, string Icon, string AttributeSchemaJson, IReadOnlySet<TaskType> AllowedTaskTypes)
{
    public static readonly AssetType Tree = new(
        "tree", "asset_type.tree", "tree",
        """
        {"type":"object","properties":{
          "genus":{"type":["string","null"],"description":"Latin genus, e.g. Tilia"},
          "species":{"type":["string","null"],"description":"Latin species"},
          "genus_raw":{"type":["string","null"],"description":"Genus exactly as delivered by the source"},
          "street_key":{"type":["string","null"]},
          "trunk_circumference_cm":{"type":["number","null"]},
          "condition":{"enum":["good","damaged","dead","gone",null]},
          "photo_url":{"type":["string","null"]},
          "quality_flags":{"type":"array","items":{"type":"string"},"description":"Data quality findings from the adapter, e.g. genus_missing"}
        }}
        """,
        new HashSet<TaskType> { TaskType.Photo, TaskType.VerifyAttribute, TaskType.Measure, TaskType.ConditionReport });

    public static readonly IReadOnlyList<AssetType> Known = [Tree];

    public static bool TryParse(string? key, out AssetType assetType)
    {
        assetType = Known.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase))!;
        return assetType is not null;
    }
}
