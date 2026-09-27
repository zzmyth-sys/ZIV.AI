using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZivAiEditor.App;

/// <summary>
/// Last-resort crash logging (8K fix hardening): an unhandled exception on any thread — including
/// an Avalonia render exception — is appended to <c>{BaseDirectory}/_cache/crash.log</c> before the
/// process dies, so a GPU/render crash leaves a diagnosable trace instead of vanishing. Never
/// throws; a logging failure must not mask the original crash.
/// </summary>
internal static class CrashLog
{
    private static int _installed;

    /// <summary>The crash log path under the temporary cache.</summary>
    public static string LogPath => Path.Combine(System.AppContext.BaseDirectory, "_cache", "crash.log");

    /// <summary>
    /// Hooks the process-wide unhandled-exception sinks once. Safe to call from several entry
    /// points (Program.Main and App init); a second call is a no-op.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.ExceptionObject as Exception, "appdomain");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception, "task");
            e.SetObserved();
        };
    }

    /// <summary>Appends one entry to the crash log. Best effort; never throws.</summary>
    public static void Write(Exception? exception, string tag)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var entry = $"[{DateTimeOffset.Now:O}] [{tag}] {exception}\n\n";
            File.AppendAllText(LogPath, entry);
        }
        catch (Exception)
        {
            // Never let logging failure escape — the process is already crashing.
        }
    }
}
