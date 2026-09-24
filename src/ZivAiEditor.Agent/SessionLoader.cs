using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>Result of loading a project (Step 9C.6-E): the restored session, its display
/// name, and any non-fatal warnings (missing images / dropped crops).</summary>
public sealed class SessionLoadResult
{
    public EditSession Session { get; init; } = null!;

    public string Name { get; init; } = "";

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

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

        if (dto.Version != SessionStore.FormatVersion)
        {
            throw new ProjectFormatException(
                dto.Version > SessionStore.FormatVersion ? "项目版本不受支持。" : "项目格式过旧。");
        }

        var warnings = new List<string>();
        var nodes = new List<EditNode>();
        foreach (var dtoNode in dto.Nodes)
        {
            var imagePath = Resolve(projectDirectory, dtoNode.ImagePath);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                warnings.Add($"节点 {ShortId(dtoNode.NodeId)} 的图片缺失，已跳过。");
                continue;
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

            nodes.Add(new EditNode
            {
                NodeId = dtoNode.NodeId,
                ParentNodeId = dtoNode.ParentNodeId,
                ImagePath = imagePath,
                Command = dtoNode.Command,
                Crop = crop,
                Mask = mask,
                CreatedAt = dtoNode.CreatedAt,
            });
        }

        NormalizeRoots(nodes, warnings);

        var session = new EditSession();
        var sessionId = string.IsNullOrWhiteSpace(dto.SessionId) ? session.SessionId : dto.SessionId;
        session.Restore(nodes, dto.CurrentNodeId, sessionId, dto.CreatedAt);

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

    private static EditNode Rebuild(EditNode node, string? parentId) => new()
    {
        NodeId = node.NodeId,
        ParentNodeId = parentId,
        ImagePath = node.ImagePath,
        Command = node.Command,
        Crop = node.Crop,
        Mask = node.Mask,
        CreatedAt = node.CreatedAt,
    };

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
