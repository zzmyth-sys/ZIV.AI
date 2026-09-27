using System.Globalization;

namespace ZivAiEditor.Tools;

/// <summary>
/// Shared parameter readers for the Qwen edit tools (Z8 / duplication cleanup): a typed lookup
/// over the string parameter bag, using invariant culture and a caller-supplied fallback.
/// </summary>
internal static class ToolParameters
{
    public static string? GetString(IReadOnlyDictionary<string, string> parameters, string key)
        => parameters.TryGetValue(key, out var value) ? value : null;

    public static int GetInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
        => parameters.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

    public static long GetLong(IReadOnlyDictionary<string, string> parameters, string key, long fallback)
        => parameters.TryGetValue(key, out var value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;

    public static double GetDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
        => parameters.TryGetValue(key, out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
}
