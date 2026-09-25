using System.Text.Json;

namespace OpenQuest.Api.Services;

public static class EnumParsing
{
    /// <summary>Parses snake_case enum names as used in the API and DB ("removed_at_source"); also accepts PascalCase.</summary>
    public static bool TryParseSnake<T>(string? value, out T result) where T : struct, Enum
    {
        foreach (var v in Enum.GetValues<T>())
            if (string.Equals(JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()), value, StringComparison.OrdinalIgnoreCase)
                || string.Equals(v.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                result = v;
                return true;
            }
        result = default;
        return false;
    }
}
