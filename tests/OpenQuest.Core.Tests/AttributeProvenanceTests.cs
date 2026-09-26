using System.Text.Json.Nodes;
using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class AttributeProvenanceTests
{
    private static JsonNode? J(string? json) => json is null ? null : JsonNode.Parse(json);

    [Fact]
    public void Without_a_contribution_the_city_s_value_is_shown()
    {
        var r = AttributeProvenance.Resolve(J("\"Tilia\""), hasContribution: false, contributed: null, replacedByContribution: null);
        Assert.Equal((DataOrigin.OpenData, false, "Tilia"), (r.Origin, r.ContributionOutdated, r.Value!.GetValue<string>()));
    }

    [Fact]
    public void A_contribution_replaces_what_the_city_had_when_the_player_contributed()
    {
        var r = AttributeProvenance.Resolve(J("null"), true, J("\"Quercus\""), J("null"));   // the city had no genus, the player found one
        Assert.Equal((DataOrigin.User, false, "Quercus"), (r.Origin, r.ContributionOutdated, r.Value!.GetValue<string>()));

        var changed = AttributeProvenance.Resolve(J("\"Baum Amt62\""), true, J("\"Tilia\""), J("\"Baum Amt62\""));
        Assert.Equal((DataOrigin.User, "Tilia"), (changed.Origin, changed.Value!.GetValue<string>()));
    }

    [Fact]
    public void A_missing_value_and_an_explicit_null_are_the_same_to_the_city()
        => Assert.Equal(DataOrigin.User, AttributeProvenance.Resolve(null, true, J("\"Tilia\""), J("null")).Origin);

    [Fact]
    public void Once_the_city_has_the_same_value_it_is_open_data_again()
    {
        var r = AttributeProvenance.Resolve(J("\"Tilia\""), true, J("\"Tilia\""), J("\"Baum Amt62\""));
        Assert.Equal((DataOrigin.OpenData, false), (r.Origin, r.ContributionOutdated));
    }

    [Fact]
    public void If_the_city_changed_the_attribute_since_the_contribution_the_city_wins_and_the_contribution_is_outdated()
    {
        var r = AttributeProvenance.Resolve(J("\"Acer\""), true, J("\"Tilia\""), J("\"Baum Amt62\""));
        Assert.Equal((DataOrigin.OpenData, true, "Acer"), (r.Origin, r.ContributionOutdated, r.Value!.GetValue<string>()));
    }

    [Fact]
    public void Lists_are_compared_by_content()
    {
        var same = AttributeProvenance.Resolve(J("[\"fungus\",\"root_lift\"]"), true, J("[\"fungus\",\"root_lift\"]"), J("null"));
        Assert.Equal(DataOrigin.OpenData, same.Origin);
        var user = AttributeProvenance.Resolve(J("null"), true, J("[\"fungus\"]"), J("null"));
        Assert.Equal(DataOrigin.User, user.Origin);
    }
}
