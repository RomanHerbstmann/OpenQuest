using System.Text;
using System.Text.Json;
using OpenQuest.Core.Domain;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class ContributionExporterTests
{
    private static readonly DateTimeOffset T = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ApprovedContribution Sample(string newGenus = "Tilia") => new(
        Guid.NewGuid(), Guid.NewGuid(), "de-muenster-trees", "tree", "abc123", new GeoPoint(51.96, 7.62),
        "genus", null, JsonSerializer.Serialize(newGenus), T);

    [Fact]
    public void GeoJson_contains_attribution_and_point_in_lon_lat_order()
    {
        var r = ContributionExporter.ToGeoJson([Sample()], "Datenquelle: Stadt Münster", T);
        using var doc = JsonDocument.Parse(r.Content);
        var root = doc.RootElement;
        Assert.Equal("Datenquelle: Stadt Münster", root.GetProperty("attribution").GetString());
        var f = root.GetProperty("features")[0];
        Assert.Equal(7.62, f.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble());
        Assert.Equal(51.96, f.GetProperty("geometry").GetProperty("coordinates")[1].GetDouble());
        var p = f.GetProperty("properties");
        Assert.Equal("genus", p.GetProperty("attribute").GetString());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("old_value").ValueKind);
        Assert.Equal("Tilia", p.GetProperty("new_value").GetString());
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void Csv_escapes_and_starts_with_attribution_comment()
    {
        var r = ContributionExporter.ToCsv([Sample("Acer, \"rubrum\"")], "Attribution", T);
        var text = Encoding.UTF8.GetString(r.Content);
        Assert.Contains("# Attribution", text);
        Assert.Contains("change_id,submission_id", text);
        Assert.Contains("\"Acer, \"\"rubrum\"\"\"", text);
    }
}
