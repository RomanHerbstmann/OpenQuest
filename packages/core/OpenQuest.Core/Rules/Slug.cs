using System.Globalization;
using System.Text;

namespace OpenQuest.Core.Rules;

/// <summary>URL-safe short names for cities and districts ("Münster-Süd" becomes "muenster-sued").</summary>
public static class Slug
{
    public static string From(string text)
    {
        var s = text.Trim().ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9')) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
