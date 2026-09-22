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
/// </summary>
public sealed class EditSession
{
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
    /// Appends a new output node under <paramref name="parentId"/> and makes it
    /// current. <paramref name="parentId"/> is <c>null</c> for a direct child of
    /// the root.
    /// </summary>
    public EditNode AppendNode(string? parentId, string imagePath, string command)
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
    public IReadOnlyList<EditNode> GetHistory()
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
}

/// <summary>One executed edit in the session DAG (INTERACTION.md §3).</summary>
public sealed class EditNode
{
    public string NodeId { get; init; } = Guid.NewGuid().ToString("N");

    public string? ParentNodeId { get; init; }

    /// <summary>The output image produced by this node (always a new file — Z24).</summary>
    public string ImagePath { get; init; } = "";

    /// <summary>The user input that produced this node (shown in the history list).</summary>
    public string Command { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
