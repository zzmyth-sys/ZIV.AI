using System;
using System.IO;

namespace ZivAiEditor.Agent.Project;

/// <summary>
/// Defense-in-depth guard for project-directory paths (batch 1 · A5). A project id is not
/// a path: it must be a single, flat directory name under the project root. A value that
/// carries a separator (<c>/</c> / <c>\</c>), a parent reference (<c>..</c>), or that
/// resolves outside the root through <see cref="Path.GetFullPath(string)"/> is rejected,
/// so a tampered <c>last_project.txt</c> / <c>session.json</c> cannot make the store read
/// or recursively delete content outside <c>sessions/</c>.
///
/// <para>Pure BCL path logic — no platform / GPU dependency — so it is unit-testable.</para>
/// </summary>
public static class PathSanitizer
{
    /// <summary>
    /// True when <paramref name="sessionId"/> is a safe single-segment project directory
    /// under <paramref name="rootDirectory"/>: non-blank, no directory separator, no
    /// <c>..</c>, and its full path stays strictly below the (full) root. Never throws.
    /// </summary>
    public static bool IsSafe(string? sessionId, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        if (sessionId.IndexOf(Path.DirectorySeparatorChar) >= 0
            || sessionId.IndexOf(Path.AltDirectorySeparatorChar) >= 0
            || sessionId.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(root, sessionId));
            return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // An unresolvable path (invalid chars, bad root) is rejected.
            return false;
        }
    }

    /// <summary>
    /// A path that structurally cannot exist as a real directory (a NUL byte is invalid in
    /// every Windows file name), returned by callers that must keep a non-throwing
    /// <c>string</c> signature for an unsafe id. Its only guarantee is that
    /// <see cref="Directory.Exists(string)"/> is <c>false</c>.
    /// </summary>
    public static string InvalidDirectory => Path.Combine(Path.GetTempPath(), "\0invalid");

    /// <summary>
    /// The project directory for <paramref name="sessionId"/> under <paramref name="rootDirectory"/>
    /// when the id is safe; otherwise <see cref="InvalidDirectory"/>. Never throws.
    /// </summary>
    public static string ResolveDirectory(string? sessionId, string rootDirectory)
        => IsSafe(sessionId, rootDirectory) ? Path.Combine(rootDirectory, sessionId!) : InvalidDirectory;
}