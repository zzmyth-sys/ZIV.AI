using System.Collections.Generic;
using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Contracts.Session;

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

    /// <summary>
    /// The node's image pack (Step 9C.10, non-empty): the root node carries the imported
    /// image(s), an edit node carries its single output. <see cref="ImagePath"/> stays the
    /// primary / pipeline image (<c>ImagePaths[0]</c>) for compatibility; a node loaded
    /// from a v1 project normalizes this to <c>[ImagePath]</c>.
    /// </summary>
    IReadOnlyList<string> ImagePaths { get; }

    /// <summary>
    /// The ordered pipeline images this edit consumed — <c>image1</c>, <c>image2</c>, … —
    /// with the main image first (Step 9C.10). Empty for the root node (no edit). Persisted
    /// so a re-run can reproduce the <c>&lt;imageN&gt;</c> mapping.
    /// </summary>
    IReadOnlyList<string> UsedImagePaths { get; }

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

    /// <summary>
    /// The per-node re-run snapshot (Step 9C.8-A): the UI resolution and reference images
    /// the edit was submitted with. <c>null</c> when the node carries neither. Everything
    /// else needed to re-run is re-derived from <see cref="Command"/> and the parent node.
    /// </summary>
    RerunSpec? Rerun { get; }

    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// End-to-end execution time of the edit that produced this node, in milliseconds
    /// (Step 9C.21). Persisted as <c>duration_ms</c>; <c>null</c> for a node loaded from a
    /// project saved before the field existed (the UI then shows no duration). Append-only;
    /// the project format stays v2.
    /// </summary>
    int? DurationMs { get; }
}
