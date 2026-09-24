using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Planning;

/// <summary>
/// Read-only view of the in-memory edit session DAG (Step 9C.5). The UI talks to
/// the session through this interface instead of the Agent's concrete
/// <c>EditSession</c> (V1), so it never references the Agent implementation type.
///
/// <para>Deliberately <b>read-only</b>: mutations (<c>SetRoot</c> / <c>ResetToRoot</c> /
/// <c>AppendNode</c> / <c>NavigateTo</c>) are not exposed here — they live on the
/// separate <see cref="IEditSessionWriter"/> so read-only consumers cannot mutate the
/// session. The concrete <c>EditSession</c> implements both.</para>
/// </summary>
public interface IEditSession
{
    string SessionId { get; }

    /// <summary>Source image the session started from; <c>null</c> for a T2I-first session.</summary>
    string? RootImagePath { get; }

    /// <summary>The working node; <c>null</c> means the root image is current.</summary>
    string? CurrentNodeId { get; }

    /// <summary>
    /// When the session was created (Step 9C.6-E); persisted as the project's
    /// <c>created_at</c>. A <see cref="IEditSessionWriter.NewSession"/> reset updates it.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// The working image path: the current node's output, or the root image when no
    /// node is current. Returns <c>null</c> when neither exists. Consumed by the
    /// command parser as the plan's source image.
    /// </summary>
    string? GetCurrentImagePath();

    /// <summary>
    /// The working <b>pipeline</b> image path (Step 9C.6-B): the current node's crop
    /// result when it has one, otherwise its <see cref="IEditNode.ImagePath"/>. This is
    /// the image an AI edit consumes. Returns <c>null</c> when no node is current.
    /// </summary>
    string? GetCurrentPipelineImagePath();

    /// <summary>
    /// The current node's hand-drawn mask (Step 9C.7), or <c>null</c> when no node is
    /// current or the node has no mask. Consumed by the command parser as the plan's mask.
    /// </summary>
    MaskSpec? GetCurrentMaskSpec();

    /// <summary>All nodes, oldest first (stable for equal timestamps).</summary>
    IReadOnlyList<IEditNode> GetHistory();

    /// <summary>
    /// The parent (reference) image path for <paramref name="imagePath"/>: the parent
    /// node's output, or the root image for a direct child of the root. Returns
    /// <c>null</c> for the root image itself (no parent) or an unknown path.
    /// </summary>
    string? GetParentImagePath(string? imagePath);

    /// <summary>
    /// The parent (reference) <b>pipeline</b> image path for <paramref name="imagePath"/>
    /// (Step 9C.6-B): the parent node's crop result when it has one, otherwise its
    /// <see cref="IEditNode.ImagePath"/>. Used by swipe-compare. Returns <c>null</c> for
    /// the root node (no parent) or an unknown path.
    /// </summary>
    string? GetParentPipelineImagePath(string? imagePath);

    /// <summary>
    /// The node path from the root image down to <see cref="CurrentNodeId"/> (oldest
    /// first). Empty when no node is current.
    /// </summary>
    IReadOnlyList<IEditNode> GetPathToCurrent();

    /// <summary>
    /// The tree depth of <paramref name="node"/> (number of ancestors: 0 for a direct
    /// child of the root). Returns 0 when <paramref name="node"/> is <c>null</c>.
    /// </summary>
    int GetDepth(IEditNode? node);
}
