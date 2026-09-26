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
          "street_key":{"type":["string","null"],"description":"5 digits, zero-padded"},
          "street_name":{"type":["string","null"]},
          "district":{"type":["string","null"],"description":"Stadtbezirk"},
          "quarter":{"type":["string","null"],"description":"Stadtteil (statistical district)"},
          "height_m":{"type":["number","null"],"description":"Object height above ground at the tree point in metres (nDOM, 95th percentile within 2.5 m); not a measured tree height"},
          "avenue_id":{"type":["string","null"],"description":"Id of the legally protected avenue (Alleenkataster NRW) the tree stands in"},
          "avenue_name":{"type":["string","null"]},
          "trunk_circumference_cm":{"type":["number","null"]},
          "condition":{"enum":["good","damaged","dead","gone",null]},
          "photo_url":{"type":["string","null"]},
          "quality_flags":{"type":"array","items":{"enum":["placeholder_genus","near_duplicate","typo_corrected","ambiguous_genus"]}}
        }}
        """,
        new HashSet<TaskType> { TaskType.Photo, TaskType.VerifyAttribute, TaskType.Measure, TaskType.ConditionReport });

    /// <summary>
    /// Legally protected natural monument (Naturdenkmal): usually one outstanding tree, sometimes a small group.
    /// Official measurements come from the city's register.
    /// </summary>
    public static readonly AssetType NaturalMonument = new(
        "natural_monument", "asset_type.natural_monument", "monument",
        """
        {"type":"object","properties":{
          "monument_number":{"type":["string","null"],"description":"Number in the city's register, e.g. 086"},
          "description":{"type":["string","null"],"description":"What is protected, e.g. '1 Platane'"},
          "genus":{"type":["string","null"],"description":"Latin genus if the monument is trees of one genus"},
          "height_m":{"type":["number","null"],"description":"Official height in metres"},
          "circumference_m":{"type":["number","null"],"description":"Official trunk circumference in metres"},
          "crown_diameter_m":{"type":["number","null"],"description":"Official crown diameter in metres"},
          "location":{"type":["string","null"]},
          "historical_context":{"type":["string","null"]},
          "landscape_context":{"type":["string","null"]},
          "condition":{"enum":["good","damaged","dead","gone",null]},
          "photo_url":{"type":["string","null"]},
          "avenue_id":{"type":["string","null"]},
          "avenue_name":{"type":["string","null"]},
          "quality_flags":{"type":"array","items":{"enum":["ambiguous_genus"]}}
        }}
        """,
        new HashSet<TaskType> { TaskType.Photo, TaskType.Measure, TaskType.ConditionReport });

    public static readonly IReadOnlyList<AssetType> Known = [Tree, NaturalMonument];

    public static bool TryParse(string? key, out AssetType assetType)
    {
        assetType = Known.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase))!;
        return assetType is not null;
    }
}
