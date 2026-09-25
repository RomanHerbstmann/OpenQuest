using OpenQuest.Adapters.Muenster;
using OpenQuest.Core.Domain;

namespace OpenQuest.Adapters.Muenster.Tests;

public class TreeCatalogParserTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "baeume-sample.json");
    private static List<Asset> LoadFixture() => TreeCatalogParser.Parse(File.OpenRead(FixturePath));

    private static string Collection(string crs, params string[] features) =>
        "{\"type\":\"FeatureCollection\",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"" + crs + "\"}},\"features\":[" +
        string.Join(",", features) + "]}";

    private static string Feature(string? strSchl, string? genus, double e = 405000, double n = 5757000) =>
        "{\"type\":\"Feature\",\"properties\":{\"str_schl\":" + Json(strSchl) + ",\"baumgruppe\":" + Json(genus) +
        "},\"geometry\":{\"type\":\"Point\",\"coordinates\":[" +
        e.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
        n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]}}";

    private static string Json(string? s) => s is null ? "null" : $"\"{s}\"";
    private const string Crs = "urn:ogc:def:crs:EPSG::25832";

    [Fact]
    public void Fixture_parses_all_features_as_wgs84_inside_muenster()
    {
        var assets = LoadFixture();
        Assert.Equal(40, assets.Count);
        Assert.All(assets, a =>
        {
            Assert.Equal("de-muenster-trees", a.DataSourceKey);
            Assert.Equal(AssetType.Tree, a.AssetType);
            Assert.InRange(a.Position.Lon, 7.487, 7.765);
            Assert.InRange(a.Position.Lat, 51.842, 52.054);
            Assert.False(string.IsNullOrEmpty(a.RawJson));
        });
    }

    [Fact]
    public void ExternalIds_are_unique_and_deterministic()
    {
        var a = LoadFixture();
        var b = LoadFixture();
        Assert.Equal(a.Select(x => x.ExternalId), b.Select(x => x.ExternalId));
        Assert.Equal(a.Count, a.Select(x => x.ExternalId).Distinct().Count());
    }

    [Fact]
    public void ExternalId_depends_on_position_only()
    {
        var a = TreeCatalogParser.Parse(Collection(Crs, Feature("02505", "Tilia", 405000.001, 5757000.002)))[0];
        var sameSpotOtherGenus = TreeCatalogParser.Parse(Collection(Crs, Feature("02505", "Acer", 405000.004, 5757000.001)))[0];
        var elsewhere = TreeCatalogParser.Parse(Collection(Crs, Feature("02505", "Tilia", 405010, 5757000)))[0];
        Assert.Equal(a.ExternalId, sameSpotOtherGenus.ExternalId); // a corrected genus must not create a "new" tree
        Assert.NotEqual(a.ExternalId, elsewhere.ExternalId);
    }

    [Theory]
    [InlineData("Baum Amt62")]
    [InlineData("Baumgruppe")]
    [InlineData("Standort")]
    [InlineData("Leerer")]
    [InlineData("Leerer Standort")]
    [InlineData("Unbekannt")]
    [InlineData("")]
    [InlineData(null)]
    public void Placeholders_become_null_genus_with_flag(string? raw)
    {
        var a = TreeCatalogParser.Parse(Collection(Crs, Feature("02505", raw)))[0];
        Assert.Null(a.Attributes["genus"]);
        Assert.Contains("placeholder_genus", a.QualityFlags);
    }

    [Theory]
    [InlineData("Catalpha", "Catalpa", null)]
    [InlineData("Cladrastris", "Cladrastis", null)]
    [InlineData("Metasequoia glyptostroboides", "Metasequoia", "glyptostroboides")]
    [InlineData("Tilia", "Tilia", null)]
    public void Genus_is_corrected_and_split(string raw, string genus, string? species)
    {
        var a = TreeCatalogParser.Parse(Collection(Crs, Feature("02505", raw)))[0];
        Assert.Equal(genus, a.Attributes["genus"]);
        Assert.Equal(species, a.Attributes["species"]);
        Assert.Equal(raw, a.Attributes["genus_raw"]);
        Assert.DoesNotContain("placeholder_genus", a.QualityFlags);
    }

    [Fact]
    public void Hybrid_suffix_is_stripped_and_flagged()
    {
        var a = TreeCatalogParser.Parse(Collection(Crs, Feature("02505", "Malus-Hybride")))[0];
        Assert.Equal("Malus", a.Attributes["genus"]);
        Assert.Equal("Malus-Hybride", a.Attributes["genus_raw"]);
        Assert.Contains("typo_corrected", a.QualityFlags);
    }

    [Theory]
    [InlineData("1005", "01005")]
    [InlineData("02505", "02505")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Street_key_is_padded_to_five_digits(string? raw, string? expected)
    {
        var a = TreeCatalogParser.Parse(Collection(Crs, Feature(raw, "Tilia")))[0];
        Assert.Equal(expected, a.Attributes["street_key"]);
    }

    [Fact]
    public void Quality_flags_only_use_the_values_of_the_data_model()
    {
        var allowed = new HashSet<string> { "placeholder_genus", "near_duplicate", "typo_corrected" };
        Assert.All(LoadFixture(), a => Assert.All(a.QualityFlags, f => Assert.Contains(f, allowed)));
        // and the fixture really exercises all of them
        var used = LoadFixture().SelectMany(a => a.QualityFlags).ToHashSet();
        Assert.Equal(allowed, used);
    }

    [Fact]
    public void Catalog_reports_source_fields_and_record_count_for_the_snapshot()
    {
        var catalog = TreeCatalogParser.ParseCatalog(File.OpenRead(FixturePath));
        Assert.Equal(["baumgruppe", "str_schl"], catalog.Fields);
        Assert.Equal(40, catalog.RecordCount);
    }

    [Fact]
    public void A_new_source_field_changes_the_schema_hash()
    {
        string One(string extra) => "{\"type\":\"Feature\",\"properties\":{\"str_schl\":\"1\",\"baumgruppe\":\"Tilia\"" + extra +
                                    "},\"geometry\":{\"type\":\"Point\",\"coordinates\":[405000,5757000]}}";
        var before = TreeCatalogParser.ParseCatalog(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Collection(Crs, One("")))));
        var after = TreeCatalogParser.ParseCatalog(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Collection(Crs, One(",\"hoehe\":12")))));
        Assert.NotEqual(OpenQuest.Core.Adapters.SourceSnapshot.Hash(before.Fields), OpenQuest.Core.Adapters.SourceSnapshot.Hash(after.Fields));
    }

    [Fact]
    public void Near_duplicates_are_flagged_in_pairs_only()
    {
        var assets = TreeCatalogParser.Parse(Collection(Crs,
            Feature("00105", "Tilia", 408322.296, 5753996.096),
            Feature("00105", "Baum Amt62", 408322.015, 5753996.629),
            Feature("00105", "Tilia", 408330.0, 5753996.0)));
        Assert.Contains("near_duplicate", assets[0].QualityFlags);
        Assert.Contains("near_duplicate", assets[1].QualityFlags);
        Assert.DoesNotContain("near_duplicate", assets[2].QualityFlags);
    }

    [Fact]
    public void Exact_duplicates_are_kept_with_distinct_ids()
    {
        var assets = TreeCatalogParser.Parse(Collection(Crs, Feature("02505", "Tilia"), Feature("02505", "Tilia")));
        Assert.Equal(2, assets.Count);
        Assert.NotEqual(assets[0].ExternalId, assets[1].ExternalId);
    }

    [Fact]
    public void Wrong_crs_fails_loudly()
        => Assert.Throws<InvalidDataException>(() =>
            TreeCatalogParser.Parse(Collection("urn:ogc:def:crs:OGC:1.3:CRS84", Feature("02505", "Tilia"))));

    [Fact]
    public void Missing_properties_fail_loudly()
    {
        var broken = """{"type":"Feature","properties":{"other":1},"geometry":{"type":"Point","coordinates":[405000,5757000]}}""";
        Assert.Throws<InvalidDataException>(() => TreeCatalogParser.Parse(Collection(Crs, broken)));
    }

    [Fact]
    public void Non_featurecollection_fails_loudly()
        => Assert.Throws<InvalidDataException>(() => TreeCatalogParser.Parse("{\"error\":\"maintenance\"}"));

    [Fact]
    public void Utm_conversion_matches_known_point()
    {
        // Münster Prinzipalmarkt ≈ 51.9625 N, 7.6256 E ≈ UTM32 E 405 ~ N 5 758 ~ (tolerance ~50 m)
        var a = TreeCatalogParser.Parse(Collection(Crs, Feature("00001", "Tilia", 405850, 5758300)))[0];
        Assert.InRange(a.Position.Lat, 51.955, 51.970);
        Assert.InRange(a.Position.Lon, 7.615, 7.640);
    }
}
