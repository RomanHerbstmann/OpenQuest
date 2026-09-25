using System.Text.RegularExpressions;

namespace OpenQuest.Adapters.Muenster.Internal;

internal sealed record NormalizedGenus(string? Genus, string? Species, IReadOnlyList<string> Flags);

/// <summary>Cleans the free-text <c>baumgruppe</c> and <c>str_schl</c> fields of the Baumkataster.</summary>
internal static partial class TreeNormalizer
{
    // The quality flags are a contract with the data model (ERD: attribute_schema of the tree asset type).
    public const string FlagPlaceholderGenus = "placeholder_genus";
    public const string FlagTypoCorrected = "typo_corrected";
    public const string FlagNearDuplicate = "near_duplicate";

    /// <summary>Values that occur instead of a genus. Matched case-insensitively.</summary>
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Baum Amt62", "Baumgruppe", "Standort", "Unbekannt",
    };

    /// <summary>Known typos in the source data (whole genus token).</summary>
    private static readonly Dictionary<string, string> Typos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Catalpha"] = "Catalpa",
        ["Cladrastris"] = "Cladrastis",
    };

    public static NormalizedGenus NormalizeGenus(string? raw)
    {
        var value = raw?.Trim() ?? "";
        if (value.Length == 0 || Placeholders.Contains(value) || value.StartsWith("Leerer", StringComparison.OrdinalIgnoreCase))
            return new NormalizedGenus(null, null, [FlagPlaceholderGenus]);

        var flags = new List<string>();

        if (HybridSuffix().IsMatch(value))
        {
            value = HybridSuffix().Replace(value, ""); // "Malus-Hybride" -> "Malus": the source value was normalized
            flags.Add(FlagTypoCorrected);
        }

        var parts = value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var genus = parts[0];
        var species = parts.Length > 1 ? parts[1] : null;

        if (Typos.TryGetValue(genus, out var fixedGenus))
        {
            genus = fixedGenus;
            if (!flags.Contains(FlagTypoCorrected)) flags.Add(FlagTypoCorrected);
        }

        return new NormalizedGenus(genus, species, flags);
    }

    /// <summary>Street key as 5-digit string (leading zeros), or null when missing.</summary>
    public static string? NormalizeStreetKey(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length >= 5 ? value : value.PadLeft(5, '0');
    }

    [GeneratedRegex(@"[-\s]Hybride$", RegexOptions.IgnoreCase)]
    private static partial Regex HybridSuffix();
}
