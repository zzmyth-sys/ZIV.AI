using System;
using System.IO;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Best-effort mask diagnostics log (R1/D). Appends to
/// <c>{AppContext.BaseDirectory}/_cache/mask.log</c> so export failures and overlay-skip
/// reasons are visible on a Release real-machine run. Never throws and never blocks the app.
/// </summary>
internal static class MaskDiagnostics
{
    private static readonly object Gate = new();

    public static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(System.AppContext.BaseDirectory, "_cache");
            Directory.CreateDirectory(directory);
            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(directory, "mask.log"), line);
            }
        }
        catch
        {
            // Diagnostics must never break the app.
        }
    }
}
