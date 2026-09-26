using ZivAiEditor.Contracts.Diagnostics;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Agent.Session;

/// <summary>
/// In-memory edit session (INTERACTION.md §3): a DAG of edit outputs rooted at the
/// source image. Since Step 9C.6-E it can be persisted as a project by the App layer
/// (<c>SessionStore</c>) and restored in place through <see cref="Restore"/>.
///
/// <para>Step 9C.6: the source image is itself the first node — <see cref="SetRoot"/> /
/// <see cref="ResetToRoot"/> synthesize a root <see cref="EditNode"/> (<c>ParentNodeId =
/// null</c>, <c>Command = "原图"</c>). The history therefore starts at the source image and
/// the user can navigate back to it.</para>
///
/// Nodes are keyed by <see cref="EditNode.NodeId"/>; <see cref="CurrentNodeId"/>
/// selects the working image, and a new edit appends a child of the current node
/// so historical branches are preserved. There is no concurrency control: the UI
/// guarantees single-threaded access.
///
/// <para>Step 9C.5: implements the Contracts read-only <see cref="IEditSession"/> and
/// the write <see cref="IEditSessionWriter"/>, so the UI can drive the session without
/// referencing this concrete type (V1).</para>
/// </summary>
public sealed partial class EditSession : IEditSession, IEditSessionWriter
{
    /// <summary>
    /// Hard ceiling for the parent / depth walks below. The graph is built
    /// one-parent-at-a-time by <see cref="AppendNode"/>, so a cycle cannot arise;
    /// the guard is purely defensive against future mutation paths (V2).
    /// </summary>
    private const int MaxTreeDepth = 4096;

    /// <summary>The command label given to the synthesized root ("source image") node.</summary>
    private const string RootCommand = "原图";

    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The synthesized root ("source image") node created by <see cref="SetRoot"/> /
    /// <see cref="ResetToRoot"/>; <c>null</c> for a node-less session (T2I-first before a
    /// root is set). It is the only node with a <c>null</c> parent.
    /// </summary>
    private EditNode? _rootNode;

    /// <summary>
    /// Source image the session started from (Step 9C.6): derived from the root node's
    /// <see cref="EditNode.ImagePath"/>. <c>null</c> when no root node exists (T2I-first
    /// session). Read-only — the root is created only through <see cref="SetRoot"/> /
    /// <see cref="ResetToRoot"/>.
    /// </summary>
    public string? RootImagePath => _rootNode?.ImagePath;

    /// <summary>
    /// The original source image the session was built from (bridge D1), persisted as the
    /// project's <c>source_image</c>. Set by <see cref="SetRoot(string)"/> /
    /// <see cref="ResetToRoot"/> / the image-pack <see cref="SetRoot(IReadOnlyList{string})"/>;
    /// restored by <c>SessionLoader</c> after <see cref="Restore"/>; cleared by
    /// <see cref="NewSession"/>.
    /// </summary>
    public string? SourceImage { get; set; }

    public Dictionary<string, EditNode> Nodes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The working node; <c>null</c> only for a session with no root node (after
    /// <see cref="SetRoot"/> / <see cref="ResetToRoot"/> this points at the root node).
    /// </summary>
    public string? CurrentNodeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Sets the session root image (Step 9C.6): rebuilds the DAG with a synthesized root
    /// node (<c>ParentNodeId = null</c>, <c>Command = "原图"</c>) and makes it current.
    /// </summary>
    public void SetRoot(string imagePath) => ResetToRoot(imagePath);

    /// <summary>
    /// Replaces the starting image and <b>resets the session</b>: every node is dropped
    /// because the existing DAG was built on the previous root, then a fresh root node is
    /// created and made current (Step 9C.3 / 9C.6). Used both at startup and when the user
    /// imports a single image as the new main image.
    /// </summary>
    public void ResetToRoot(string imagePath)
    {
        Nodes.Clear();

        _rootNode = new EditNode
        {
            ParentNodeId = null,
            ImagePath = imagePath,
            ImagePaths = new[] { imagePath },
            Command = RootCommand,
        };

        Nodes[_rootNode.NodeId] = _rootNode;
        CurrentNodeId = _rootNode.NodeId;
        SourceImage = imagePath;
    }

    /// <summary>
    /// Resets this session to a brand-new empty session <b>in place</b> (module-boundary
    /// migration step 2): drops every node, the root and the current selection, and adopts a
    /// fresh <see cref="SessionId"/> / <see cref="CreatedAt"/>. Equivalent to the initial state
    /// of a newly constructed <see cref="EditSession"/>.
    /// </summary>
    public void NewSession()
    {
        Nodes.Clear();
        _rootNode = null;
        CurrentNodeId = null;
        SessionId = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.Now;
        SourceImage = null;
    }

    /// <summary>
    /// Replaces this session's contents <b>in place</b> (Step 9C.6-E) from a persisted
    /// project: clears the DAG, rebuilds it from <paramref name="nodes"/>, re-points the
    /// root (the single node with no parent) and selects <paramref name="currentId"/>
    /// (falling back to the root when it is unknown), then adopts
    /// <paramref name="sessionId"/> / <paramref name="createdAt"/>. The instance identity
    /// is preserved so UI consumers that captured <see cref="IEditSession"/> /
    /// <see cref="IEditSessionWriter"/> stay valid.
    /// </summary>
    public void Restore(
        IReadOnlyList<IEditNode> nodes,
        string? currentId,
        string sessionId,
        DateTimeOffset createdAt)
    {
        Nodes.Clear();
        foreach (var node in nodes)
        {
            Nodes[node.NodeId] = new EditNode
            {
                NodeId = node.NodeId,
                ParentNodeId = node.ParentNodeId,
                ImagePath = node.ImagePath,
                ImagePaths = NormalizeImages(node.ImagePaths, node.ImagePath),
                UsedImagePaths = NormalizeImages(node.UsedImagePaths, null),
                Command = node.Command,
                Crop = node.Crop,
                Mask = node.Mask,
                Rerun = node.Rerun,
                CreatedAt = node.CreatedAt,
                DurationMs = node.DurationMs,
            };
        }

        _rootNode = Nodes.Values.FirstOrDefault(node => string.IsNullOrEmpty(node.ParentNodeId));
        CurrentNodeId = !string.IsNullOrEmpty(currentId) && Nodes.ContainsKey(currentId)
            ? currentId
            : _rootNode?.NodeId;
        SessionId = sessionId;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Appends a new output node under <paramref name="parentId"/> and makes it current.
    /// A <c>null</c> <paramref name="parentId"/> attaches to the root node when one exists
    /// (defensive branch: the production path always passes the current node, which is the
    /// root after <see cref="SetRoot"/>); with no root node it stays parent-less.
    /// </summary>
    public IEditNode AppendNode(string? parentId, string imagePath, string command)
    {
        if (string.IsNullOrEmpty(parentId) && _rootNode is not null)
        {
            parentId = _rootNode.NodeId;
        }

        var node = new EditNode
        {
            ParentNodeId = parentId,
            ImagePath = imagePath,
            ImagePaths = new[] { imagePath },
            Command = command,
        };

        Nodes[node.NodeId] = node;
        CurrentNodeId = node.NodeId;
        return node;
    }

    /// <summary>Switches the working node. Returns <c>false</c> when the id is unknown.</summary>
    public bool NavigateTo(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.ContainsKey(nodeId))
        {
            return false;
        }

        CurrentNodeId = nodeId;
        return true;
    }

    /// <summary>
    /// Sets (or clears) the intrinsic crop of one node (Step 9C.6-B). Because
    /// <see cref="EditNode"/> is immutable (<c>init</c>-only), the node is rebuilt with the
    /// same identity / parent / image / command / timestamp and swapped in. A no-op when
    /// <paramref name="nodeId"/> is unknown (defensive: the caller is the preview / close
    /// path, which must never throw). The root reference is re-pointed when the root node
    /// is updated, so <see cref="RootImagePath"/> stays valid.
    ///
    /// <para>Step 9C.7 (D2): when the crop actually <b>changes</b> the node's mask is
    /// cleared, because the mask coordinates belong to the previous pipeline canvas. An
    /// identical re-set preserves the mask.</para>
    /// </summary>
    public void SetNodeCrop(string nodeId, CropSpec? crop)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var cropChanged = !CropEquals(node.Crop, crop);
        var updated = node with
        {
            Crop = crop,
            Mask = cropChanged ? null : node.Mask,
        };

        Nodes[nodeId] = updated;
        if (ReferenceEquals(_rootNode, node))
        {
            _rootNode = updated;
        }
    }

    /// <summary>
    /// Sets (or clears) the hand-drawn mask of one node (Step 9C.7). Like
    /// <see cref="SetNodeCrop"/> the node is rebuilt in place with the same identity /
    /// parent / image / command / timestamp and its crop preserved. A no-op when
    /// <paramref name="nodeId"/> is unknown.
    /// </summary>
    public void SetNodeMask(string nodeId, MaskSpec? mask)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var updated = node with
        {
            Mask = mask,
        };

        Nodes[nodeId] = updated;
        if (ReferenceEquals(_rootNode, node))
        {
            _rootNode = updated;
        }
    }

    /// <summary>
    /// Sets (or clears) the re-run snapshot of one node (Step 9C.8-A). Like the crop /
    /// mask the node is rebuilt in place with the same identity / parent / image / command
    /// / timestamp and its crop / mask preserved. A no-op when <paramref name="nodeId"/> is
    /// unknown.
    /// </summary>
    public void SetNodeRerun(string nodeId, RerunSpec? rerun)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var updated = node with
        {
            Rerun = rerun,
        };

        Nodes[nodeId] = updated;
        if (ReferenceEquals(_rootNode, node))
        {
            _rootNode = updated;
        }
    }

    /// <summary>
    /// Replaces a node's output image in place (Step 9C.8-A2). The node is rebuilt with the
    /// same identity / parent / command / timestamp / crop / mask / re-run snapshot; only
    /// <see cref="EditNode.ImagePath"/> changes. A no-op when <paramref name="nodeId"/> is
    /// unknown.
    /// </summary>
    public void ReplaceNodeImage(string nodeId, string newImagePath)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var updated = node with
        {
            ImagePath = newImagePath,
            ImagePaths = new[] { newImagePath },
        };

        Nodes[nodeId] = updated;
        if (ReferenceEquals(_rootNode, node))
        {
            _rootNode = updated;
        }
    }

    /// <summary>
    /// Sets the end-to-end execution time (milliseconds) of one node (Step 9C.21). The node
    /// is rebuilt in place with every other field preserved. A no-op when
    /// <paramref name="nodeId"/> is unknown. <c>null</c> clears it.
    /// </summary>
    public void SetNodeDurationMs(string nodeId, int? durationMs)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var updated = node with { DurationMs = durationMs };
        Nodes[nodeId] = updated;
        if (ReferenceEquals(_rootNode, node))
        {
            _rootNode = updated;
        }
    }

    /// <summary>
    /// The current node's hand-drawn mask (Step 9C.7), or <c>null</c> when no node is
    /// current or the node has no mask. Consumed by the command parser as the plan's mask.
    /// </summary>
    public MaskSpec? GetCurrentMaskSpec()
    {
        if (string.IsNullOrEmpty(CurrentNodeId) || !Nodes.TryGetValue(CurrentNodeId, out var node))
        {
            return null;
        }

        return node.Mask;
    }

    private static bool CropEquals(CropSpec? a, CropSpec? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.X == b.X
               && a.Y == b.Y
               && a.Width == b.Width
               && a.Height == b.Height
               && string.Equals(a.ResultImagePath, b.ResultImagePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// All nodes, oldest first (stable for equal timestamps). Since Step 9C.6 the
    /// synthesized root ("原图") node is included and always sorts first, so the history
    /// starts at the source image even on a coarse clock.
    /// </summary>
    public IReadOnlyList<IEditNode> GetHistory()
        => Nodes.Values
            .OrderBy(node => node.ParentNodeId is null ? 0 : 1)
            .ThenBy(node => node.CreatedAt)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The working image path: the current node's output (the root node's image when the
    /// root is current). Returns <c>null</c> when no node is current.
    /// </summary>
    public string? GetCurrentImagePath()
    {
        if (!string.IsNullOrEmpty(CurrentNodeId)
            && Nodes.TryGetValue(CurrentNodeId, out var node)
            && !string.IsNullOrWhiteSpace(node.ImagePath))
        {
            return node.ImagePath;
        }

        return null;
    }

    /// <summary>
    /// The working <b>pipeline</b> image path (Step 9C.6-B): the current node's crop
    /// result when it has one, otherwise its <see cref="EditNode.ImagePath"/>. Returns
    /// <c>null</c> when no node is current.
    /// </summary>
    public string? GetCurrentPipelineImagePath()
    {
        if (string.IsNullOrEmpty(CurrentNodeId) || !Nodes.TryGetValue(CurrentNodeId, out var node))
        {
            return null;
        }

        return PipelinePath(node);
    }

    /// <summary>
    /// The parent (reference) image path for <paramref name="imagePath"/>, used by the
    /// swipe-compare overlay (V2, moved down from the UI): the parent node's output.
    /// Returns <c>null</c> for the root node itself (no parent) or an unknown path
    /// (Step 9C.6: the root is a node now, not a special fallback).
    /// </summary>
    public string? GetParentImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        foreach (var node in GetHistory())
        {
            if (!string.Equals(node.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.IsNullOrEmpty(node.ParentNodeId)
                ? null
                : Nodes.TryGetValue(node.ParentNodeId, out var parent)
                    ? parent.ImagePath
                    : null;
        }

        return null;
    }

    /// <summary>
    /// The parent (reference) <b>pipeline</b> image path for <paramref name="imagePath"/>
    /// (Step 9C.6-B): the parent node's crop result when it has one, otherwise its
    /// <see cref="EditNode.ImagePath"/>. Used by swipe-compare so the reference is the
    /// image the edit actually consumed. Returns <c>null</c> for the root node (no parent)
    /// or an unknown path.
    /// </summary>
    public string? GetParentPipelineImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        foreach (var node in GetHistory())
        {
            // The input may be the node's own image or its crop result (chat / preview can
            // carry either), so match against both.
            if (!MatchesPath(node, imagePath))
            {
                continue;
            }

            return string.IsNullOrEmpty(node.ParentNodeId)
                ? null
                : Nodes.TryGetValue(node.ParentNodeId, out var parent)
                    ? PipelinePath(parent)
                    : null;
        }

        return null;
    }

    /// <summary>The image a node feeds the pipeline: its crop result, else its output.</summary>
    private static string PipelinePath(IEditNode node)
    {
        var crop = node.Crop;
        var hasResult = crop is { ResultImagePath.Length: > 0 };
        var result = hasResult ? crop!.ResultImagePath : node.ImagePath;
        if (DiagLog.IsEnabled)
        {
            // D1 diag (observation only): which branch was picked and whether the crop file exists.
            DiagLog.Log(
                $"D1 PipelinePath node={node.NodeId} {DiagLog.DescribeCrop(crop)} "
                + $"picked={(hasResult ? "cropResult" : "imagePath")} result={result}");
        }

        return result;
    }

    /// <summary>True when <paramref name="path"/> is the node's own image or its crop result.</summary>
    private static bool MatchesPath(IEditNode node, string path)
        => string.Equals(node.ImagePath, path, StringComparison.OrdinalIgnoreCase)
           || (node.Crop is { ResultImagePath.Length: > 0 } crop
               && string.Equals(crop.ResultImagePath, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The node path from the root node down to <see cref="CurrentNodeId"/> (oldest
    /// first). For the root node itself this is <c>[root]</c>; empty when no node is
    /// current. Used by the UI to replay the chat context (V2, moved down from the UI);
    /// the <see cref="MaxTreeDepth"/> guard is defensive.
    /// </summary>
    public IReadOnlyList<IEditNode> GetPathToCurrent()
    {
        var path = new List<IEditNode>();
        var id = CurrentNodeId;
        var guard = 0;
        while (!string.IsNullOrEmpty(id)
               && Nodes.TryGetValue(id, out var node)
               && guard++ < MaxTreeDepth)
        {
            path.Add(node);
            id = node.ParentNodeId;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// The tree depth of <paramref name="node"/> (number of ancestors: 0 for the root
    /// node, 1 for a direct child of the root). Used by the UI for history indentation
    /// (V2, moved down from the UI). Returns 0 when <paramref name="node"/> is <c>null</c>.
    /// </summary>
    public int GetDepth(IEditNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        var depth = 0;
        var id = node.ParentNodeId;
        var guard = 0;
        while (!string.IsNullOrEmpty(id)
               && Nodes.TryGetValue(id, out var parent)
               && guard++ < MaxTreeDepth)
        {
            depth++;
            id = parent.ParentNodeId;
        }

        return depth;
    }
}
