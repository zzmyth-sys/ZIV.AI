using System.Collections.Generic;
using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Session;

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
    /// Sets the session root image <b>pack</b> (Step 9C.10): the root node's
    /// <see cref="IEditNode.ImagePaths"/> becomes <paramref name="imagePaths"/> (blank entries
    /// dropped, order kept) and its <see cref="IEditNode.ImagePath"/> is <c>imagePaths[0]</c>.
    /// As with <see cref="SetRoot(string)"/> the existing DAG is reset; an empty / all-blank
    /// list is a no-op.
    /// </summary>
    void SetRoot(IReadOnlyList<string> imagePaths);

    /// <summary>
    /// Replaces the starting image and <b>resets the session</b>: every node and the
    /// current-node selection are dropped (Step 9C.3).
    /// </summary>
    void ResetToRoot(string imagePath);

    /// <summary>
    /// Resets this session to a brand-new empty session <b>in place</b> (module-boundary
    /// migration step 2): the DAG, the root and the current-node selection are dropped, and a
    /// fresh <see cref="IEditSession.SessionId"/> / <see cref="IEditSession.CreatedAt"/> are
    /// adopted. The instance identity is preserved so consumers holding
    /// <see cref="IEditSession"/> / <see cref="IEditSessionWriter"/> stay valid. Equivalent to
    /// the initial state of a newly constructed session.
    /// </summary>
    void NewSession();

    /// <summary>
    /// Replaces this session's contents <b>in place</b> from a persisted project (Step 9C.6-E):
    /// clears the DAG, rebuilds it from <paramref name="nodes"/>, re-points the root (the single
    /// node with no parent) and selects <paramref name="currentId"/> (falling back to the root
    /// when unknown), then adopts <paramref name="sessionId"/> / <paramref name="createdAt"/>.
    /// The instance identity is preserved so UI consumers stay valid.
    /// </summary>
    void Restore(
        IReadOnlyList<IEditNode> nodes,
        string? currentId,
        string sessionId,
        DateTimeOffset createdAt);

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

    /// <summary>
    /// Sets (or clears) the hand-drawn mask of one node (Step 9C.7). Like the crop it is a
    /// node property — no node is added, and the node's identity / parent / image are
    /// preserved. A no-op when <paramref name="nodeId"/> is unknown.
    /// </summary>
    void SetNodeMask(string nodeId, MaskSpec? mask);

    /// <summary>
    /// Sets (or clears) the re-run snapshot of one node (Step 9C.8-A). Like the crop /
    /// mask it is a node property — no node is added, and the node's identity / parent /
    /// image / crop / mask are preserved. A no-op when <paramref name="nodeId"/> is unknown.
    /// </summary>
    void SetNodeRerun(string nodeId, RerunSpec? rerun);

    /// <summary>
    /// Sets the ordered pipeline images a node's edit consumed (Step 9C.10) —
    /// <c>image1</c>, <c>image2</c>, … with the main image first. Like the crop / mask /
    /// rerun it is a node property: the node is rebuilt in place with the same identity /
    /// parent / image(s) / command / timestamp / crop / mask / rerun, so no node is added.
    /// Blank entries are dropped. A no-op when <paramref name="nodeId"/> is unknown.
    /// </summary>
    void SetNodeUsedImages(string nodeId, IReadOnlyList<string> imagePaths);

    /// <summary>
    /// Replaces a node's output image <b>in place</b> (Step 9C.8-A2): the node is rebuilt
    /// with the same identity / parent / command / crop / mask / re-run snapshot; only
    /// <see cref="IEditNode.ImagePath"/> changes. No node is added. A no-op when
    /// <paramref name="nodeId"/> is unknown.
    /// </summary>
    void ReplaceNodeImage(string nodeId, string newImagePath);

    /// <summary>
    /// Removes the whole subtree <b>below</b> <paramref name="nodeId"/> — every descendant,
    /// while the node itself is preserved — and returns the removed nodes so the caller can
    /// clean up their files (Step 9C.8-A2). Returns an empty list when
    /// <paramref name="nodeId"/> is unknown (no-op, matching <see cref="SetNodeCrop"/>).
    /// </summary>
    IReadOnlyList<IEditNode> RemoveSubtree(string nodeId);
}
