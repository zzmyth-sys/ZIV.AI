namespace ZivAiEditor.Contracts.Planning;

/// <summary>
/// Mutation surface of the in-memory edit session DAG (Step 9C.5). Split from the
/// read-only <see cref="IEditSession"/> so read-only consumers (compare overlay,
/// future mask / preview) cannot mutate the session; only the session owner
/// (<c>SessionViewModel</c>) depends on this writer. The concrete <c>EditSession</c>
/// implements both interfaces.
/// </summary>
public interface IEditSessionWriter
{
    /// <summary>Sets the session root image (does not create a node).</summary>
    void SetRoot(string imagePath);

    /// <summary>
    /// Replaces the starting image and <b>resets the session</b>: every node and the
    /// current-node selection are dropped (Step 9C.3).
    /// </summary>
    void ResetToRoot(string imagePath);

    /// <summary>
    /// Appends a new output node under <paramref name="parentId"/> and makes it
    /// current. <paramref name="parentId"/> is <c>null</c> for a direct child of
    /// the root.
    /// </summary>
    IEditNode AppendNode(string? parentId, string imagePath, string command);

    /// <summary>Switches the working node. Returns <c>false</c> when the id is unknown.</summary>
    bool NavigateTo(string nodeId);

    /// <summary>
    /// Sets (or clears) the intrinsic crop of one node (Step 9C.6-B). The node is
    /// rebuilt in place — no node is added, and the node's identity / parent / image are
    /// preserved. A no-op when <paramref name="nodeId"/> is unknown.
    /// </summary>
    void SetNodeCrop(string nodeId, CropSpec? crop);
}
