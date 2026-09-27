using System;
using System.Diagnostics;
using System.IO;

namespace ZivAiEditor.Imaging;

/// <summary>
/// Shared best-effort directory cleanup for the per-session temp roots (Z8 / duplication
/// cleanup): delete one session directory, or every session directory under a root. Never
/// throws; each file delete is guarded so a locked file cannot abort the rest. The
/// <paramref name="tag"/> keeps the per-domain log prefix (<c>crop</c> / <c>mask</c>).
/// </summary>
internal static class DirectoryCleanup
{
    public static void CleanupSession(string? rootDirectory, string? sessionId, string tag)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        DeleteDirectorySafe(Path.Combine(rootDirectory!, sessionId), tag);
    }

    public static void CleanupAll(string rootDirectory, string tag)
    {
        try
        {
            if (!Directory.Exists(rootDirectory))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(rootDirectory))
            {
                DeleteDirectorySafe(directory, tag);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{tag}] cleanup-all failed: {ex.Message}");
        }
    }

    private static void DeleteDirectorySafe(string directory, string tag)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{tag}] delete failed '{file}': {ex.Message}");
                }
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{tag}] cleanup failed '{directory}': {ex.Message}");
        }
    }
}
