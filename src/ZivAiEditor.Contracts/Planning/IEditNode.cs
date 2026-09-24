using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Planning;

/// <summary>
/// Read-only projection of one executed edit in the session DAG (Step 9C.5). The
/// concrete node lives in the Agent layer (<c>EditNode</c>); the UI consumes only
/// this interface, so it no longer references the Agent implementation type (V1).
///
/// Fields mirror the frozen <c>EditNode</c> exactly — no geometry is added here
/// (precise outpaint geometry is a separate follow-up).
/// </summary>
public interface IEditNode
{
    /// <summary>Stable node id (unique within the session).</summary>
    string NodeId { get; }

    /// <summary>Parent node id; <c>null</c> for a direct child of the root image.</summary>
    string? ParentNodeId { get; }

    /// <summary>The output image produced by this node (always a new file — Z24).</summary>
    string ImagePath { get; }

    /// <summary>The user input that produced this node (shown in the history list).</summary>
    string Command { get; }

    /// <summary>
    /// The node's intrinsic crop (Step 9C.6-B): at most one per node, re-adjustable,
    /// never a node of its own. <c>null</c> when the node is uncropped.
    /// </summary>
    CropSpec? Crop { get; }

    /// <summary>
    /// The node's hand-drawn mask (Step 9C.7): at most one per node, re-drawable, never a
    /// node of its own. <c>null</c> when the node has no mask. Coordinates are the node's
    /// current pipeline (crop-result) image pixels.
    /// </summary>
    MaskSpec? Mask { get; }

    DateTimeOffset CreatedAt { get; }
}
