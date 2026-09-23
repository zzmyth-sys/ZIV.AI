using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ZivAiEditor.Agent;

/// <summary>
/// Exports an in-memory <see cref="EditSession"/> to a directory when the app
/// closes (INTERACTION.md §4). It lives in the Agent layer, alongside the
/// <see cref="EditSession"/> it operates on, and has no platform dependency —
/// only BCL file IO and JSON.
/// </summary>
public interface ISessionExporter
{
    /// <summary>
    /// Writes <c>session.json</c> plus the node images into
    /// <paramref name="outputDirectory"/> and returns that directory. Returns
    /// <c>null</c> on any failure instead of throwing, so a failed export can
    /// never block shutdown.
    /// </summary>
    Task<string?> ExportAsync(
        EditSession session,
        string outputDirectory,
        CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="ISessionExporter"/>: serializes the session DAG to
/// <c>session.json</c> and copies every node image as <c>{NodeId}.png</c>. The
/// session is only read, never modified. All failures are swallowed and reported
/// as <c>null</c> (INTERACTION.md §4: "不报错").
/// </summary>
public sealed class SessionExporter : ISessionExporter
{
    private const string SessionFileName = "session.json";

    // Keep the exported JSON human-readable: do not escape non-ASCII prompts /
    // commands (this file is local and never rendered as HTML). A single context
    // instance is reused because JsonSerializerOptions becomes read-only once used.
    private static readonly SessionExportJsonContext JsonContext =
        new(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public async Task<string?> ExportAsync(
        EditSession session,
        string outputDirectory,
        CancellationToken ct = default)
    {
        try
        {
            if (session is null || string.IsNullOrWhiteSpace(outputDirectory))
            {
                return null;
            }

            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputDirectory);

            var dto = SessionExportDto.From(session);

            foreach (var node in session.GetHistory())
            {
                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(node.ImagePath) && File.Exists(node.ImagePath))
                {
                    var destination = Path.Combine(outputDirectory, node.NodeId + ".png");
                    File.Copy(node.ImagePath, destination, overwrite: true);
                }

                // Step 9C.6-B: copy the node's crop result as {NodeId}_crop.png so the
                // export is self-contained (the JSON points at this relative name).
                if (node.Crop is { ResultImagePath.Length: > 0 } crop && File.Exists(crop.ResultImagePath))
                {
                    var cropDestination = Path.Combine(outputDirectory, node.NodeId + "_crop.png");
                    File.Copy(crop.ResultImagePath, cropDestination, overwrite: true);
                }
            }

            var json = JsonSerializer.Serialize(dto, JsonContext.SessionExportDto);
            var jsonPath = Path.Combine(outputDirectory, SessionFileName);
            await File.WriteAllTextAsync(jsonPath, json, ct).ConfigureAwait(false);

            return outputDirectory;
        }
        catch (Exception ex)
        {
            // A failed export must never surface an exception to the close path.
            Debug.WriteLine($"[session] export failed: {ex.Message}");
            return null;
        }
    }
}

internal sealed class SessionExportDto
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";

    [JsonPropertyName("root_image_path")]
    public string? RootImagePath { get; init; }

    [JsonPropertyName("current_node_id")]
    public string? CurrentNodeId { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("nodes")]
    public List<SessionExportNode> Nodes { get; init; } = new();

    public static SessionExportDto From(EditSession session) => new()
    {
        SessionId = session.SessionId,
        RootImagePath = session.RootImagePath,
        CurrentNodeId = session.CurrentNodeId,
        CreatedAt = session.CreatedAt,
        Nodes = session.GetHistory()
            .Select(node => new SessionExportNode
            {
                NodeId = node.NodeId,
                ParentNodeId = node.ParentNodeId,
                ImagePath = node.ImagePath,
                Command = node.Command,
                CreatedAt = node.CreatedAt,
                Crop = node.Crop is { } crop
                    ? new SessionExportCrop
                    {
                        X = crop.X,
                        Y = crop.Y,
                        Width = crop.Width,
                        Height = crop.Height,
                        ResultImagePath = crop.ResultImagePath.Length > 0
                            ? node.NodeId + "_crop.png"
                            : "",
                    }
                    : null,
            })
            .ToList(),
    };
}

internal sealed class SessionExportNode
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; init; } = "";

    [JsonPropertyName("parent_node_id")]
    public string? ParentNodeId { get; init; }

    [JsonPropertyName("image_path")]
    public string ImagePath { get; init; } = "";

    [JsonPropertyName("command")]
    public string Command { get; init; } = "";

    [JsonPropertyName("crop")]
    public SessionExportCrop? Crop { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class SessionExportCrop
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    /// <summary>Result image name relative to the export directory ({NodeId}_crop.png).</summary>
    [JsonPropertyName("result_image_path")]
    public string ResultImagePath { get; init; } = "";
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
[JsonSerializable(typeof(SessionExportDto))]
internal partial class SessionExportJsonContext : JsonSerializerContext
{
}
