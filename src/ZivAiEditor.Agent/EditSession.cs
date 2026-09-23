using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Agent;

/// <summary>
/// In-memory edit session (INTERACTION.md §3): a DAG of edit outputs rooted at an
/// optional source image. It is intentionally <b>not persisted</b> — the session
/// lives only for the process lifetime and is exported on close by the App layer
/// (<c>ISessionExporter</c>).
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

    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Source image the session started from; <c>null</c> for a T2I-first session.</summary>
    public string? RootImagePath { get; set; }

    public Dictionary<string, EditNode> Nodes { get; } = new(StringComparer.Ordinal);

    /// <summary>The working node; <c>null</c> means the root image is current.</summary>
    public string? CurrentNodeId { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Sets the session root image (does not create a node).</summary>
    public void SetRoot(string imagePath) => RootImagePath = imagePath;

    /// <summary>
    /// Replaces the starting image and <b>resets the session</b>: every node and the
    /// current-node selection are dropped, because the existing DAG was built on the
    /// previous root. Used when the user imports a single image as the new main image
    /// (Step 9C.3).
    /// </summary>
    public void ResetToRoot(string imagePath)
    {
        RootImagePath = imagePath;
        Nodes.Clear();
        CurrentNodeId = null;
    }

    /// <summary>
    /// Appends a new output node under <paramref name="parentId"/> and makes it
    /// current. <paramref name="parentId"/> is <c>null</c> for a direct child of
    /// the root.
    /// </summary>
    public IEditNode AppendNode(string? parentId, string imagePath, string command)
    {
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

    /// <summary>All nodes, oldest first (stable for equal timestamps).</summary>
    public IReadOnlyList<IEditNode> GetHistory()
        => Nodes.Values
            .OrderBy(node => node.CreatedAt)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The working image path: the current node's output, or the root image when
    /// no node is current. Returns <c>null</c> when neither exists.
    /// </summary>
    public string? GetCurrentImagePath()
    {
        if (!string.IsNullOrEmpty(CurrentNodeId)
            && Nodes.TryGetValue(CurrentNodeId, out var node)
            && !string.IsNullOrWhiteSpace(node.ImagePath))
        {
            return node.ImagePath;
        }

        return RootImagePath;
    }

    /// <summary>
    /// The parent (reference) image path for <paramref name="imagePath"/>, used by the
    /// swipe-compare overlay (V2, moved down from the UI): the parent node's output, or
    /// the root image for a direct child of the root. Returns <c>null</c> for the root
    /// image itself (no parent) or an unknown path.
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

            if (string.IsNullOrEmpty(node.ParentNodeId))
            {
                return RootImagePath;
            }

            return Nodes.TryGetValue(node.ParentNodeId, out var parent)
                ? parent.ImagePath
                : null;
        }

        return null;
    }

    /// <summary>
    /// The node path from the root image down to <see cref="CurrentNodeId"/> (oldest
    /// first). Empty when no node is current. Used by the UI to replay the chat context
    /// (V2, moved down from the UI); the <see cref="MaxTreeDepth"/> guard is defensive.
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
    /// The tree depth of <paramref name="node"/> (number of ancestors: 0 for a direct
    /// child of the root). Used by the UI for history indentation (V2, moved down from
    /// the UI). Returns 0 when <paramref name="node"/> is <c>null</c>.
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
