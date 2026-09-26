namespace OpenQuest.Core.Rules;

/// <summary>Brings a genus as typed by a player or delivered by a source to one spelling, so that "tilia" and "Tilia cordata" count as "Tilia".</summary>
public static class GenusName
{
    /// <summary>The genus (first word) with a capital first letter and the rest in lower case; null for empty input.</summary>
    public static string? Normalize(string? text)
    {
        var word = text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(word)) return null;
        word = word.Trim('.', ',', ';', '(', ')');
        return word.Length == 0 ? null : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }
}
