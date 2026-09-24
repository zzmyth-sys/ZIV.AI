using System;
using System.Collections.Generic;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Image-pack members of <see cref="SessionViewModel"/> (Step 9C.10-P2): the multi-image
/// "原图" root, its consumption as pipeline references, the current pack size used by the
/// command pre-gate, and the non-error informational chat line. Split out of
/// <c>SessionViewModel.cs</c> to keep each file within the Z8 budget.
/// </summary>
public sealed partial class SessionViewModel
{
    /// <summary>Hard cap on the root image pack (Step 9C.10, Q2=B).</summary>
    private const int MaxRootImages = 10;

    /// <summary>
    /// Sets the starting image <b>pack</b> and resets the session (Step 9C.10): every node and
    /// the current-node selection are dropped, because the DAG was built on the previous root.
    /// Blank entries are ignored and the pack is trimmed to <see cref="MaxRootImages"/> (a hint
    /// is shown when trimmed). An empty / all-blank list is a no-op.
    /// </summary>
    public void SetRootImage(IReadOnlyList<string>? imagePaths)
    {
        var paths = NormalizeRootImages(imagePaths);
        if (paths.Count == 0)
        {
            return;
        }

        var trimmed = paths.Count > MaxRootImages;
        if (trimmed)
        {
            paths = paths.GetRange(0, MaxRootImages);
        }

        // Resetting the root drops the existing DAG, so its crop / mask temp files are orphans.
        ImageCropper.CleanupSession(_session.SessionId);
        MaskExporter.CleanupSession(_session.SessionId);
        _writer.SetRoot(paths);
        RefreshHistory();
        RebuildContext();

        if (trimmed)
        {
            AddInfo($"最多支持 {MaxRootImages} 张原图，多余的已忽略");
        }
    }

    /// <summary>
    /// Sets a single starting image and resets the session (Step 9C.3). Delegates to the pack
    /// overload; a blank path is a no-op.
    /// </summary>
    public void SetRootImage(string? imagePath)
        => SetRootImage(string.IsNullOrWhiteSpace(imagePath) ? null : new[] { imagePath! });

    /// <summary>
    /// The current node's image-pack size (Step 9C.10); 0 when no node is current. Drives the
    /// command image requirement (<c>CommandRequirements</c>) and the send gating.
    /// </summary>
    public int CurrentImageCount
    {
        get
        {
            var path = _session.GetPathToCurrent();
            return path.Count > 0 ? path[^1].ImagePaths.Count : 0;
        }
    }

    /// <summary>
    /// Appends a non-error informational line to the chat stream (Step 9C.10, R2). Distinct
    /// from <see cref="AddHint"/>, which renders as an error-style hint.
    /// </summary>
    public void AddInfo(string text)
        => Messages.Add(new ChatMessage { Role = ChatRole.System, Text = text, IsError = false });

    /// <summary>The current node (last on the root → current path), or <c>null</c> when empty.</summary>
    private IEditNode? CurrentNode()
    {
        var path = _session.GetPathToCurrent();
        return path.Count > 0 ? path[^1] : null;
    }

    /// <summary>The current node's pack (the root's imported images); empty when no node.</summary>
    private IReadOnlyList<string> CurrentPack()
        => CurrentNode()?.ImagePaths ?? Array.Empty<string>();

    /// <summary>
    /// The current node's pack images beyond the main (<c>ImagePaths[1..]</c>): the references
    /// a multi-image root contributes. Empty for a single-image node / an edit node.
    /// </summary>
    private IReadOnlyList<string> CurrentPackExtras()
    {
        var pack = CurrentPack();
        if (pack.Count <= 1)
        {
            return Array.Empty<string>();
        }

        var extras = new List<string>(pack.Count - 1);
        for (var i = 1; i < pack.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(pack[i]))
            {
                extras.Add(pack[i]);
            }
        }

        return extras;
    }

    /// <summary>
    /// Assembles the pipeline references (Step 9C.10): the root pack's extras first, then the
    /// attachment references, truncated to <paramref name="max"/> (the tail is dropped).
    /// <paramref name="truncated"/> reports whether anything was dropped.
    /// </summary>
    private static IReadOnlyList<string> AssembleReferences(
        IReadOnlyList<string> packExtras,
        IReadOnlyList<string> attachmentRefs,
        int max,
        out bool truncated)
    {
        var all = new List<string>(packExtras.Count + attachmentRefs.Count);
        all.AddRange(packExtras);
        all.AddRange(attachmentRefs);

        truncated = all.Count > max;
        if (truncated)
        {
            all.RemoveRange(max, all.Count - max);
        }

        return all;
    }

    /// <summary>
    /// The display pack a node shows in the chat (Step 9C.10): the pipeline main
    /// (crop-aware) first, then the root's extra images. A single-image node yields one entry.
    /// </summary>
    private static IReadOnlyList<string> BuildDisplayPack(IEditNode node)
    {
        var pack = node.ImagePaths;
        var display = new List<string>(pack.Count) { PipelinePath(node) };
        for (var i = 1; i < pack.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(pack[i]))
            {
                display.Add(pack[i]);
            }
        }

        return display;
    }

    /// <summary>Drops blank root images and keeps order; a <c>null</c> / empty list yields empty.</summary>
    private static List<string> NormalizeRootImages(IReadOnlyList<string>? imagePaths)
    {
        var kept = new List<string>();
        if (imagePaths is null)
        {
            return kept;
        }

        foreach (var path in imagePaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                kept.Add(path);
            }
        }

        return kept;
    }
}
