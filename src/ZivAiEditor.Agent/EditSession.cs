using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// In-memory edit session (INTERACTION.md §3): a DAG of edit outputs rooted at the
/// source image. It is intentionally <b>not persisted</b> — the session lives only for
/// the process lifetime and is exported on close by the App layer
/// (<c>ISessionExporter</c>).
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

    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

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

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

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

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
