using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZivAiEditor.App;

/// <summary>Bridge notify status values (bridge §9).</summary>
internal static class NotifyStatus
{
    public const string Success = "success";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
    public const string Busy = "busy";
}

/// <summary>
/// The status document the bridge writes for the caller (ZIV), see bridge §9. Field names are the
/// frozen snake_case contract; <c>error</c> is serialized even when <c>null</c>.
/// </summary>
internal sealed class NotifyMessage
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = NotifyStatus.Error;

    [JsonPropertyName("source_image")]
    public string? SourceImage { get; init; }

    [JsonPropertyName("template")]
    public string? Template { get; init; }

    [JsonPropertyName("output_path")]
    public string? OutputPath { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; init; }

    /// <summary>True on the headless path; false when the editor ran the quick task in place.</summary>
    [JsonPropertyName("exited")]
    public bool Exited { get; init; }
}

/// <summary>Source-generated JSON for the notify document (AOT-friendly).</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(NotifyMessage))]
internal partial class NotifyJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Writes the bridge notify document atomically (bridge §9): a sibling temp file followed by
/// <see cref="File.Move(string, string, bool)"/> with overwrite, so a reader never observes a
/// half-written file. Failures are swallowed — the caller (ZIV) has its own 30&#160;min timeout.
/// </summary>
internal static class NotifyWriter
{
    /// <summary>Writes <paramref name="message"/> to <paramref name="path"/> (no-op when blank). Never throws.</summary>
    public static void TryWrite(string? path, NotifyMessage message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Write(path!, message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ZIV-AI-NOTIFY] write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Atomic write (temp + move-overwrite). Throws on IO failure.</summary>
    public static void Write(string path, NotifyMessage message)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(message, NotifyJsonContext.Default.NotifyMessage);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
