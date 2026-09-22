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

            foreach (var node in dto.Nodes)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(node.ImagePath) || !File.Exists(node.ImagePath))
                {
                    continue;
                }

                var destination = Path.Combine(outputDirectory, node.NodeId + ".png");
                File.Copy(node.ImagePath, destination, overwrite: true);
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

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true)]
[JsonSerializable(typeof(SessionExportDto))]
internal partial class SessionExportJsonContext : JsonSerializerContext
{
}
