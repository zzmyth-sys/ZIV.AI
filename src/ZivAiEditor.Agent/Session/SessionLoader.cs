using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Agent.Session;

/// <summary>
/// Rebuilds an <see cref="EditSession"/> from a project's <c>session.json</c> (Step 9C.6-E).
/// Relative image names are resolved against <c>projectDirectory</c>. A node whose image is
/// missing is skipped (with a warning); a crop whose result image is missing is dropped (so
/// the pipeline falls back to the node image). Version mismatches throw
/// <see cref="ProjectFormatException"/>; malformed JSON throws
/// <see cref="ProjectCorruptException"/>.
/// </summary>
public static class SessionLoader
{
    public static SessionLoadResult LoadFromJson(string json, string projectDirectory)
    {
        SessionFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, SessionStoreJsonContext.Default.SessionFileDto);
        }
        catch (JsonException ex)
        {
            throw new ProjectCorruptException("项目文件不是有效的 JSON。", ex);
        }

        if (dto is null)
        {
            throw new ProjectCorruptException("项目文件为空。");
        }

        if (dto.Version < 1 || dto.Version > SessionStore.FormatVersion)
        {
            throw new ProjectFormatException(
                dto.Version > SessionStore.FormatVersion ? "项目版本不受支持。" : "项目格式过旧。");
        }

        var warnings = new List<string>();
        var nodes = new List<EditNode>();
        foreach (var dtoNode in dto.Nodes ?? new List<SessionFileNode>())
        {
            // Format v2 stores the node's pack in `image_paths`; a v1 project has the legacy
            // single `image_path`, normalized to a one-element pack (Step 9C.10).
            var dtoImages = dtoNode.ImagePaths is { Count: > 0 }
                ? dtoNode.ImagePaths
                : string.IsNullOrWhiteSpace(dtoNode.LegacyImagePath)
                    ? new List<string>()
                    : new List<string> { dtoNode.LegacyImagePath! };

            var imagePaths = new List<string>(dtoImages.Count);
            var missing = 0;
            foreach (var imageName in dtoImages)
            {
                var resolved = Resolve(projectDirectory, imageName);
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                {
                    imagePaths.Add(resolved);
                }
                else
                {
                    missing++;
                }
            }

            if (imagePaths.Count == 0)
            {
                warnings.Add($"节点 {ShortId(dtoNode.NodeId)} 的图片缺失，已跳过。");
                continue;
            }

            if (missing > 0)
            {
                warnings.Add($"节点 {ShortId(dtoNode.NodeId)} 有 {missing} 张图片缺失，已忽略。");
            }

            CropSpec? crop = null;
            if (dtoNode.Crop is { } dtoCrop)
            {
                var cropPath = Resolve(projectDirectory, dtoCrop.ResultImagePath);
                if (!string.IsNullOrWhiteSpace(cropPath) && File.Exists(cropPath))
                {
                    crop = new CropSpec
                    {
                        X = dtoCrop.X,
                        Y = dtoCrop.Y,
                        Width = dtoCrop.Width,
                        Height = dtoCrop.Height,
                        SourceWidth = dtoCrop.SourceWidth,
                        SourceHeight = dtoCrop.SourceHeight,
                        ResultImagePath = cropPath,
                    };
                }
                else
                {
                    warnings.Add($"节点 {ShortId(dtoNode.NodeId)} 的裁切图缺失，已忽略裁切。");
                }
            }

            MaskSpec? mask = null;
            if (dtoNode.Mask is { } dtoMask)
            {
                var maskPath = Resolve(projectDirectory, dtoMask.ImagePath);
                if (!string.IsNullOrWhiteSpace(maskPath) && File.Exists(maskPath))
                {
                    mask = new MaskSpec
                    {
                        MaskImagePath = maskPath,
                        Width = dtoMask.Width,
                        Height = dtoMask.Height,
                        IsBinary = dtoMask.IsBinary,
                        Invert = dtoMask.Invert,
                        FeatherPx = dtoMask.FeatherPx,
                    };
                }
                else
                {
                    warnings.Add($"节点 {ShortId(dtoNode.NodeId)} 的遮罩图缺失，已忽略遮罩。");
                }
            }

            RerunSpec? rerun = null;
            if (dtoNode.Rerun is { } dtoRerun)
            {
                var references = new List<string>();
                foreach (var referenceName in dtoRerun.AdditionalImages ?? new List<string>())
                {
                    var referencePath = Resolve(projectDirectory, referenceName);
                    if (!string.IsNullOrWhiteSpace(referencePath) && File.Exists(referencePath))
                    {
                        references.Add(referencePath);
                    }
                    else
                    {
                        warnings.Add($"节点 {ShortId(dtoNode.NodeId)} 的参考图缺失，已忽略。");
                    }
                }

                var resolution = FromDto(dtoRerun.Resolution);
                if (resolution is not null || references.Count > 0)
                {
                    rerun = new RerunSpec { Resolution = resolution, AdditionalImages = references };
                }
            }

            nodes.Add(new EditNode
            {
                NodeId = dtoNode.NodeId,
                ParentNodeId = dtoNode.ParentNodeId,
                ImagePath = imagePaths[0],
                ImagePaths = imagePaths,
                UsedImagePaths = ResolveList(projectDirectory, dtoNode.UsedImagePaths, dtoNode.NodeId, warnings),
                Command = dtoNode.Command,
                Crop = crop,
                Mask = mask,
                Rerun = rerun,
                CreatedAt = dtoNode.CreatedAt,
                DurationMs = dtoNode.DurationMs,
            });
        }

        NormalizeRoots(nodes, warnings);

        var session = new EditSession();
        var sessionId = string.IsNullOrWhiteSpace(dto.SessionId) ? session.SessionId : dto.SessionId;
        session.Restore(nodes, dto.CurrentNodeId, sessionId, dto.CreatedAt);

        // Bridge D1 (P1-1): Restore's signature is frozen, so the source-image reverse-lookup key
        // is set on the concrete EditSession after the restore. Missing / blank → null (legacy).
        session.SourceImage = string.IsNullOrWhiteSpace(dto.SourceImage) ? null : dto.SourceImage;

        var name = string.IsNullOrWhiteSpace(dto.Name) ? "未命名" : dto.Name;
        return new SessionLoadResult { Session = session, Name = name, Warnings = warnings };
    }

    /// <summary>Guarantees exactly one parent-less node; extra roots are re-parented to the
    /// first, and a root-less graph promotes its first node.</summary>
    private static void NormalizeRoots(List<EditNode> nodes, List<string> warnings)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var rootIndexes = new List<int>();
        for (var index = 0; index < nodes.Count; index++)
        {
            if (string.IsNullOrEmpty(nodes[index].ParentNodeId))
            {
                rootIndexes.Add(index);
            }
        }

        if (rootIndexes.Count == 0)
        {
            nodes[0] = Rebuild(nodes[0], null);
            warnings.Add("项目缺少起始节点，已将第一个节点视为起始图。");
            return;
        }

        if (rootIndexes.Count > 1)
        {
            var rootId = nodes[rootIndexes[0]].NodeId;
            foreach (var index in rootIndexes.Skip(1))
            {
                nodes[index] = Rebuild(nodes[index], rootId);
            }

            warnings.Add("项目存在多个起始节点，已合并为一个。");
        }
    }

    private static EditNode Rebuild(EditNode node, string? parentId)
        => node with { ParentNodeId = parentId };

    /// <summary>
    /// Resolves a list of relative image names against the project directory (Step 9C.10);
    /// a name whose file is missing is dropped with a warning.
    /// </summary>
    private static IReadOnlyList<string> ResolveList(
        string projectDirectory,
        List<string>? names,
        string nodeId,
        List<string> warnings)
    {
        var result = new List<string>();
        if (names is null)
        {
            return result;
        }

        foreach (var name in names)
        {
            var path = Resolve(projectDirectory, name);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                result.Add(path);
            }
            else
            {
                warnings.Add($"节点 {ShortId(nodeId)} 的输入图缺失，已忽略。");
            }
        }

        return result;
    }

    private static ResolutionPolicy? FromDto(SessionFileResolution? dto)
    {
        if (dto is null)
        {
            return null;
        }

        var mode = Enum.TryParse<ResolutionMode>(dto.Mode, ignoreCase: true, out var parsed)
            ? parsed
            : ResolutionMode.Side;

        return new ResolutionPolicy
        {
            Mode = mode,
            Side = dto.Side,
            Area = dto.Area,
            Scale = dto.Scale,
            Width = dto.Width,
            Height = dto.Height,
            MaxPixels = dto.MaxPixels,
        };
    }

    private static string Resolve(string projectDirectory, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        return Path.IsPathRooted(name) ? name : Path.Combine(projectDirectory, name);
    }

    private static string ShortId(string nodeId) => nodeId.Length <= 8 ? nodeId : nodeId[..8];
}
