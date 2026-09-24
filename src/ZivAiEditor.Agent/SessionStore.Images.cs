using System;
using System.Collections.Generic;
using System.IO;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// Image-copy helpers for <see cref="SessionStore"/> (Step 9C.10): copying a node's image
/// pack / references / used images into a project directory, reusing an already-copied file
/// instead of duplicating it. Split out of <c>SessionStore.cs</c> to keep each file within
/// the Z8 budget.
/// </summary>
public sealed partial class SessionStore
{
    /// <summary>
    /// Copies a node's reference images into the project's <c>refs/</c> folder as
    /// <c>{nodeId}_ref{n}{ext}</c> and returns their <b>relative</b> names (Step 9C.8-A).
    /// Missing sources are skipped. The destination folder is created on demand.
    /// </summary>
    private static List<string> CopyReferenceImages(
        string directory,
        Dictionary<string, string> copied,
        string nodeId,
        IReadOnlyList<string> sources)
    {
        var names = new List<string>(sources.Count);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                continue;
            }

            var extension = Path.GetExtension(source);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".png";
            }

            var relative = $"refs/{nodeId}_ref{index + 1}{extension}";
            CopyNodeImage(directory, copied, source, relative);
            names.Add(relative);
        }

        return names;
    }

    /// <summary>
    /// Copies one image into the project under <paramref name="relativeName"/> and records
    /// the source → relative mapping, so a later use of the same source reuses the file
    /// instead of duplicating it (Step 9C.10). A blank / missing source is skipped.
    /// </summary>
    private static void CopyNodeImage(
        string directory,
        Dictionary<string, string> copied,
        string? source,
        string relativeName)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var destination = Path.Combine(directory, relativeName.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        CopyIfNeeded(source, destination);
        Remember(copied, source, relativeName);
    }

    /// <summary>
    /// Maps a node's used pipeline images to project-relative names (Step 9C.10): reuse an
    /// already-copied file when known (the parent output / crop, or a same-node reference),
    /// otherwise copy it into <c>used/{nodeId}_{n}{ext}</c>. The main-first order is kept.
    /// </summary>
    private static List<string> CopyUsedImages(
        string directory,
        Dictionary<string, string> copied,
        IEditNode node)
    {
        var names = new List<string>(node.UsedImagePaths.Count);
        for (var index = 0; index < node.UsedImagePaths.Count; index++)
        {
            var source = node.UsedImagePaths[index];
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            if (TryRemembered(copied, source, out var known))
            {
                names.Add(known);
                continue;
            }

            var extension = Path.GetExtension(source);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".png";
            }

            var relative = $"used/{node.NodeId}_{index + 1}{extension}";
            CopyNodeImage(directory, copied, source, relative);
            names.Add(relative);
        }

        return names;
    }

    private static void Remember(Dictionary<string, string> copied, string source, string relative)
    {
        try
        {
            copied[Path.GetFullPath(source)] = relative;
        }
        catch (Exception)
        {
            // An unresolvable path is simply not remembered; a later lookup just misses.
        }
    }

    private static bool TryRemembered(Dictionary<string, string> copied, string source, out string relative)
    {
        relative = "";
        try
        {
            return copied.TryGetValue(Path.GetFullPath(source), out relative!);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
