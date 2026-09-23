using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

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
public sealed class EditSession : IEditSession, IEditSessionWriter
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
            Command = RootCommand,
        };

        Nodes[_rootNode.NodeId] = _rootNode;
        CurrentNodeId = _rootNode.NodeId;
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
                Command = node.Command,
                Crop = node.Crop,
                CreatedAt = node.CreatedAt,
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
    /// </summary>
    public void SetNodeCrop(string nodeId, CropSpec? crop)
    {
        if (string.IsNullOrEmpty(nodeId) || !Nodes.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var updated = new EditNode
        {
            NodeId = node.NodeId,
            ParentNodeId = node.ParentNodeId,
            ImagePath = node.ImagePath,
            Command = node.Command,
            CreatedAt = node.CreatedAt,
            Crop = crop,
        };

        Nodes[nodeId] = updated;
        if (ReferenceEquals(_rootNode, node))
        {
            _rootNode = updated;
        }
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
        => node.Crop is { ResultImagePath.Length: > 0 } crop ? crop.ResultImagePath : node.ImagePath;

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

/// <summary>One executed edit in the session DAG (INTERACTION.md §3).</summary>
public sealed class EditNode : IEditNode
{
    public string NodeId { get; init; } = Guid.NewGuid().ToString("N");

    public string? ParentNodeId { get; init; }

    /// <summary>The output image produced by this node (always a new file — Z24).</summary>
    public string ImagePath { get; init; } = "";

    /// <summary>The user input that produced this node (shown in the history list).</summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// The node's intrinsic crop (Step 9C.6-B); <c>null</c> when uncropped. At most one
    /// per node — re-adjusting replaces it, never appends a node.
    /// </summary>
    public CropSpec? Crop { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
