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
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>One saved project as shown in the project list (Step 9C.6-E).</summary>
public sealed record ProjectInfo(string SessionId, string Name, DateTimeOffset CreatedAt);

/// <summary>Raised when a project's <c>version</c> is not supported (Step 9C.6-E).</summary>
public sealed class ProjectFormatException : Exception
{
    public ProjectFormatException(string message) : base(message)
    {
    }
}

/// <summary>Raised when a project file is missing / unreadable / malformed (Step 9C.6-E).</summary>
public sealed class ProjectCorruptException : Exception
{
    public ProjectCorruptException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Persistence for edit sessions as self-contained <b>projects</b> (Step 9C.6-E). A project
/// lives under the program directory (Z14) at <c>sessions/{sessionId}/</c> and holds
/// <c>session.json</c> plus every node image as <c>{NodeId}.png</c> (and <c>{NodeId}_{n}.png</c>
/// for the extra images of a node pack), its crop as <c>{NodeId}_crop.png</c>, its mask as
/// <c>{NodeId}_mask.png</c>, re-run references under <c>refs/</c>, and re-run pipeline inputs
/// under <c>used/</c> (Step 9C.10). The JSON stores <b>relative</b> image names so the project
/// is portable; the loader resolves them against the project directory.
///
/// <para>Replaces the Step 8 <c>SessionExporter</c>; <see cref="ExportToAsync"/> keeps the
/// "save a copy elsewhere" capability for a later UI. Pure BCL file IO / JSON — no platform
/// or GPU dependency — so it is unit-testable.</para>
/// </summary>
public sealed partial class SessionStore
{
    public const int FormatVersion = 2;

    private const string SessionFileName = "session.json";
    private const string LastProjectFileName = "last_project.txt";
    private const string UnnamedProject = "未命名";

    private static readonly SessionStoreJsonContext Json = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <param name="rootDirectory">
    /// Overrides the project root (tests use a temp dir). Defaults to
    /// <c>{AppContext.BaseDirectory}/sessions</c> (Z14: data ships with the program).
    /// </param>
    public SessionStore(string? rootDirectory = null)
        => RootDirectory = rootDirectory ?? Path.Combine(AppContext.BaseDirectory, "sessions");

    /// <summary>The directory holding every project.</summary>
    public string RootDirectory { get; }

    /// <summary>The directory of one project.</summary>
    public string GetProjectDirectory(string sessionId) => Path.Combine(RootDirectory, sessionId);

    /// <summary>
    /// Writes the session as a project (overwriting an existing one) and returns its info.
    /// Images are copied as relative names; a source already inside the project directory
    /// is left in place. A missing source image is skipped (the node is still recorded).
    /// </summary>
    public async Task<ProjectInfo> SaveAsync(EditSession session, string name, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var directory = GetProjectDirectory(session.SessionId);
        await WriteProjectAsync(directory, session, name, ct).ConfigureAwait(false);
        return new ProjectInfo(session.SessionId, name, session.CreatedAt);
    }

    /// <summary>
    /// Loads a project into a fresh <see cref="EditSession"/>. Throws
    /// <see cref="ProjectFormatException"/> on an unsupported version and
    /// <see cref="ProjectCorruptException"/> on a missing / malformed file.
    /// </summary>
    public async Task<SessionLoadResult> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var directory = GetProjectDirectory(sessionId);
        var path = Path.Combine(directory, SessionFileName);
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            throw new ProjectCorruptException("无法读取项目文件。", ex);
        }

        return SessionLoader.LoadFromJson(json, directory);
    }

    /// <summary>Scans the project root and returns the readable projects, newest first.</summary>
    public async Task<IReadOnlyList<ProjectInfo>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<ProjectInfo>();
        if (!Directory.Exists(RootDirectory))
        {
            return result;
        }

        foreach (var directory in Directory.GetDirectories(RootDirectory))
        {
            ct.ThrowIfCancellationRequested();
            var info = await TryReadInfoAsync(directory, ct).ConfigureAwait(false);
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result.OrderByDescending(project => project.CreatedAt).ToArray();
    }

    /// <summary>Deletes one project directory. Never throws.</summary>
    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
        => Task.Run(
            () =>
            {
                try
                {
                    var directory = GetProjectDirectory(sessionId);
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[session] delete failed '{sessionId}': {ex.Message}");
                }
            },
            ct);

    /// <summary>Renames a saved project (rewrites <c>name</c> in its JSON). No-op when missing.</summary>
    public async Task RenameAsync(string sessionId, string name, CancellationToken ct = default)
    {
        var directory = GetProjectDirectory(sessionId);
        var path = Path.Combine(directory, SessionFileName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize(json, Json.SessionFileDto);
            if (dto is null)
            {
                return;
            }

            var updated = new SessionFileDto
            {
                Version = dto.Version,
                Name = name,
                SessionId = dto.SessionId,
                CurrentNodeId = dto.CurrentNodeId,
                CreatedAt = dto.CreatedAt,
                Nodes = dto.Nodes,
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(updated, Json.SessionFileDto), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[session] rename failed '{sessionId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the session to an arbitrary external directory (the future "save as"). Returns
    /// the directory, or <c>null</c> on any failure (never throws).
    /// </summary>
    public async Task<string?> ExportToAsync(
        EditSession session,
        string name,
        string externalDirectory,
        CancellationToken ct = default)
    {
        if (session is null || string.IsNullOrWhiteSpace(externalDirectory))
        {
            return null;
        }

        try
        {
            await WriteProjectAsync(externalDirectory, session, name, ct).ConfigureAwait(false);
            return externalDirectory;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[session] export failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>The last opened project id, or <c>null</c> when none / unreadable.</summary>
    public string? GetLastProjectId()
    {
        try
        {
            var path = Path.Combine(RootDirectory, LastProjectFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();
            return value.Length == 0 ? null : value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[session] read last-project failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Records (or clears, when blank) the last opened project id. Never throws.</summary>
    public async Task SetLastProjectIdAsync(string? sessionId, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var path = Path.Combine(RootDirectory, LastProjectFileName);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            await File.WriteAllTextAsync(path, sessionId.Trim(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[session] write last-project failed: {ex.Message}");
        }
    }

    private static async Task WriteProjectAsync(
        string directory,
        EditSession session,
        string name,
        CancellationToken ct)
    {
        Directory.CreateDirectory(directory);

        var nodes = new List<SessionFileNode>();
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in session.GetHistory())
        {
            ct.ThrowIfCancellationRequested();

            var pack = node.ImagePaths.Count > 0 ? node.ImagePaths : new[] { node.ImagePath };
            var imageNames = new List<string>(pack.Count);
            for (var index = 0; index < pack.Count; index++)
            {
                var imageName = index == 0 ? node.NodeId + ".png" : $"{node.NodeId}_{index + 1}.png";
                CopyNodeImage(directory, copied, pack[index], imageName);
                imageNames.Add(imageName);
            }

            SessionFileCrop? crop = null;
            if (node.Crop is { } spec)
            {
                var cropName = node.NodeId + "_crop.png";
                CopyNodeImage(directory, copied, spec.ResultImagePath, cropName);
                crop = new SessionFileCrop
                {
                    X = spec.X,
                    Y = spec.Y,
                    Width = spec.Width,
                    Height = spec.Height,
                    ResultImagePath = cropName,
                };
            }

            SessionFileRerun? rerun = null;
            if (node.Rerun is { } rerunSpec
                && (rerunSpec.Resolution is not null || rerunSpec.AdditionalImages.Count > 0))
            {
                rerun = new SessionFileRerun
                {
                    Resolution = ToDto(rerunSpec.Resolution),
                    AdditionalImages = CopyReferenceImages(directory, copied, node.NodeId, rerunSpec.AdditionalImages),
                };
            }

            SessionFileMask? mask = null;
            if (node.Mask is { } maskSpec && !string.IsNullOrWhiteSpace(maskSpec.MaskImagePath))
            {
                var maskName = node.NodeId + "_mask.png";
                CopyNodeImage(directory, copied, maskSpec.MaskImagePath, maskName);
                mask = new SessionFileMask
                {
                    ImagePath = maskName,
                    Width = maskSpec.Width,
                    Height = maskSpec.Height,
                    IsBinary = maskSpec.IsBinary,
                    Invert = maskSpec.Invert,
                    FeatherPx = maskSpec.FeatherPx,
                };
            }

            nodes.Add(new SessionFileNode
            {
                NodeId = node.NodeId,
                ParentNodeId = node.ParentNodeId,
                ImagePaths = imageNames,
                UsedImagePaths = CopyUsedImages(directory, copied, node),
                Command = node.Command,
                Crop = crop,
                Mask = mask,
                Rerun = rerun,
                CreatedAt = node.CreatedAt,
            });
        }

        var dto = new SessionFileDto
        {
            Version = FormatVersion,
            Name = string.IsNullOrWhiteSpace(name) ? UnnamedProject : name,
            SessionId = session.SessionId,
            CurrentNodeId = session.CurrentNodeId,
            CreatedAt = session.CreatedAt,
            Nodes = nodes,
        };

        var json = JsonSerializer.Serialize(dto, Json.SessionFileDto);
        await File.WriteAllTextAsync(Path.Combine(directory, SessionFileName), json, ct).ConfigureAwait(false);
    }

    private static SessionFileResolution? ToDto(ResolutionPolicy? policy)
        => policy is null
            ? null
            : new SessionFileResolution
            {
                Mode = policy.Mode.ToString(),
                Side = policy.Side,
                Area = policy.Area,
                Scale = policy.Scale,
                Width = policy.Width,
                Height = policy.Height,
                MaxPixels = policy.MaxPixels,
            };

    private static void CopyIfNeeded(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return;
        }

        try
        {
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        catch (Exception)
        {
            return;
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static async Task<ProjectInfo?> TryReadInfoAsync(string directory, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(directory, SessionFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // Formats 1..current are listed: v1 (single image_path) still opens after the
            // v2 image-pack upgrade; a missing / future version is skipped.
            if (!root.TryGetProperty("version", out var versionElement)
                || !versionElement.TryGetInt32(out var version)
                || version < 1
                || version > FormatVersion)
            {
                return null;
            }

            var id = root.TryGetProperty("session_id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Path.GetFileName(directory);
            }

            var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var created = root.TryGetProperty("created_at", out var createdElement)
                          && createdElement.TryGetDateTimeOffset(out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            return new ProjectInfo(id!, string.IsNullOrWhiteSpace(name) ? UnnamedProject : name!, created);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed class SessionFileDto
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";

    [JsonPropertyName("current_node_id")]
    public string? CurrentNodeId { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("nodes")]
    public List<SessionFileNode> Nodes { get; init; } = new();
}

internal sealed class SessionFileNode
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; init; } = "";

    [JsonPropertyName("parent_node_id")]
    public string? ParentNodeId { get; init; }

    /// <summary>
    /// Relative image names of the node's pack (format v2, Step 9C.10): <c>{NodeId}.png</c>
    /// for the first image and <c>{NodeId}_{n}.png</c> for the rest, resolved against the
    /// project dir.
    /// </summary>
    [JsonPropertyName("image_paths")]
    public List<string> ImagePaths { get; init; } = new();

    /// <summary>
    /// Relative names of the ordered pipeline images the edit consumed (format v2,
    /// Step 9C.10), main first; empty for the root node.
    /// </summary>
    [JsonPropertyName("used_image_paths")]
    public List<string> UsedImagePaths { get; init; } = new();

    /// <summary>
    /// Legacy single image name (format v1); read-only — never written in v2 (the
    /// <see cref="JsonIgnoreCondition.WhenWritingNull"/> keeps a fresh file clean).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("image_path")]
    public string? LegacyImagePath { get; init; }

    [JsonPropertyName("command")]
    public string Command { get; init; } = "";

    [JsonPropertyName("crop")]
    public SessionFileCrop? Crop { get; init; }

    [JsonPropertyName("mask")]
    public SessionFileMask? Mask { get; init; }

    /// <summary>Optional re-run snapshot (Step 9C.8-A); absent in older projects.</summary>
    [JsonPropertyName("rerun")]
    public SessionFileRerun? Rerun { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class SessionFileCrop
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("result_image_path")]
    public string ResultImagePath { get; init; } = "";
}

internal sealed class SessionFileMask
{
    /// <summary>Relative mask image name (<c>{NodeId}_mask.png</c>), resolved against the project dir.</summary>
    [JsonPropertyName("image_path")]
    public string ImagePath { get; init; } = "";

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("is_binary")]
    public bool IsBinary { get; init; } = true;

    [JsonPropertyName("invert")]
    public bool Invert { get; init; }

    /// <summary>
    /// User-explicit feather radius in image pixels (Step 9C.7-B), appended at format
    /// version 1. Absent in older files → default <c>0</c> (hard mask).
    /// </summary>
    [JsonPropertyName("feather_px")]
    public int FeatherPx { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SessionFileDto))]
[JsonSerializable(typeof(SessionFileMask))]
[JsonSerializable(typeof(SessionFileRerun))]
[JsonSerializable(typeof(SessionFileResolution))]
internal partial class SessionStoreJsonContext : JsonSerializerContext
{
}
