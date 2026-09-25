using OpenQuest.Adapters.Muenster;

namespace OpenQuest.Adapters.Muenster.Tests;

/// <summary>
/// Optional: point OPENQUEST_FULL_DATASET at a downloaded WFS GeoJSON dump to check the parser against
/// the real data (expected numbers from Research Note 0001). Silently passes when unset.
/// </summary>
public class FullDatasetSmokeTests
{
    [Fact]
    public void Real_dataset_matches_research_note_profile()
    {
        var path = Environment.GetEnvironmentVariable("OPENQUEST_FULL_DATASET");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var assets = TreeCatalogParser.Parse(File.OpenRead(path));
        Assert.Equal(43_114, assets.Count);
        Assert.Equal(assets.Count, assets.Select(a => a.ExternalId).Distinct().Count());

        var missing = assets.Count(a => a.QualityFlags.Contains("genus_missing"));
        Assert.InRange(missing, 3_500, 3_650); // ~3,580 (8.3 %)
        Assert.InRange(assets.Count(a => a.QualityFlags.Contains("near_duplicate")), 80, 110); // 48 pairs
        Assert.InRange(assets.Count(a => a.QualityFlags.Contains("street_key_missing")), 10, 20); // 14
        Assert.Contains(assets, a => a.Attributes["genus"] == "Catalpa" && a.Attributes["genus_raw"] == "Catalpha");
    }
}
