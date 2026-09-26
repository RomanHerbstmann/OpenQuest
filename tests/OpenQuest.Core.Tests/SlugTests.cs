using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Münster", "muenster")]
    [InlineData("Mitte-Süd", "mitte-sued")]
    [InlineData("  Gievenbeck / Nord  ", "gievenbeck-nord")]
    [InlineData("Straße & Co.", "strasse-co")]
    [InlineData("Café Zentrum", "cafe-zentrum")]
    [InlineData("---", "")]
    public void Names_become_url_safe_keys(string name, string slug) => Assert.Equal(slug, Slug.From(name));
}
