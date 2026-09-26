using OpenQuest.Core.Rules;

namespace OpenQuest.Core.Tests;

public class GenusNameTests
{
    [Theory]
    [InlineData("Tilia", "Tilia")]
    [InlineData("tilia", "Tilia")]
    [InlineData("  QUERCUS  ", "Quercus")]
    [InlineData("Tilia cordata", "Tilia")]
    [InlineData("acer platanoides L.", "Acer")]
    [InlineData("(Malus)", "Malus")]
    public void The_genus_is_the_first_word_in_a_common_spelling(string input, string expected)
        => Assert.Equal(expected, GenusName.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Nothing_is_no_genus(string? input) => Assert.Null(GenusName.Normalize(input));
}
