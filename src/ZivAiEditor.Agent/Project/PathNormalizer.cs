using System;
using System.IO;

namespace ZivAiEditor.Agent.Project;

/// <summary>
/// Path normalization for the source-image → project reverse lookup (bridge §4.2). Full path plus
/// an ordinal (case-insensitive) comparison; kept <c>internal</c> so the bridge does not open a
/// public contract type for one helper. Exposed to the App (the reverse-lookup consumer) through
/// <c>InternalsVisibleTo</c>.
/// </summary>
internal static class PathNormalizer
{
    /// <summary>
    /// The absolute canonical form of <paramref name="path"/>. Returns the trimmed input unchanged
    /// when it is blank or cannot be resolved (the comparison below still works textually).
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    /// <summary>True when both paths denote the same file (normalized, ordinal case-insensitive).</summary>
    public static bool AreSame(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
