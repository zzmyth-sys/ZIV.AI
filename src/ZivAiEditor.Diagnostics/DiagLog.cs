using System.Globalization;
using System.IO;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Diagnostics;

/// <summary>
/// Temporary, opt-in diagnostics log for the <c>/扩图</c> re-run investigation (hooks D1–D7).
/// It only appends lines to <c>{AppContext.BaseDirectory}/_cache/diag.log</c>: never throws,
/// never blocks and never changes control flow (callers log and continue). Production stays
/// silent unless the process is started with <c>ZIV_AI_DIAG=1</c>.
///
/// <para><b>Diagnostics-only, to be removed with the hooks.</b> Batch 2A / D1 moved it out of
/// Contracts into this dedicated assembly so the contract layer stays behavior-free (Contracts
/// never references Diagnostics). Agent / App / Backend reference this assembly directly (Z8:
/// one helper, not scattered per assembly).</para>
/// </summary>
public static class DiagLog
{
    private const string EnvVar = "ZIV_AI_DIAG";
    private static readonly object Gate = new();
    private static readonly bool Enabled = string.Equals(
        Environment.GetEnvironmentVariable(EnvVar), "1", StringComparison.Ordinal);

    /// <summary>True when <c>ZIV_AI_DIAG=1</c> was set at process start.</summary>
    public static bool IsEnabled => Enabled;

    /// <summary>Appends one <c>[diag]</c> line; a no-op unless enabled; never throws.</summary>
    public static void Log(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            var directory = Path.Combine(System.AppContext.BaseDirectory, "_cache");
            var line = string.Concat(
                "[diag] ",
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                " ",
                message,
                Environment.NewLine);
            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "diag.log"), line);
            }
        }
        catch
        {
            // Diagnostics must never affect the app.
        }
    }

    /// <summary>Never-throwing <see cref="File.Exists(string)"/> for hook fields.</summary>
    public static bool Exists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A compact, single-token crop description for hook lines.</summary>
    public static string DescribeCrop(CropSpec? crop)
        => crop is null
            ? "crop=null"
            : $"crop{{result={crop.ResultImagePath},outpaint={crop.IsOutpaint()},exists={Exists(crop.ResultImagePath)}}}";
}
