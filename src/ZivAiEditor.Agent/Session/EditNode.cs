using System;
using System.Collections.Generic;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Agent.Session;

/// <summary>
/// One executed edit in the session DAG (INTERACTION.md §3). A nominal record, so the
/// in-place updates in <see cref="EditSession"/> are expressed with <c>node with { … }</c>
/// instead of re-listing every field. Split out of <c>EditSession.cs</c> (Step 9C.21) to
/// keep each file within the Z8 budget.
/// </summary>
public sealed record EditNode : IEditNode
{
    public string NodeId { get; init; } = Guid.NewGuid().ToString("N");

    public string? ParentNodeId { get; init; }

    /// <summary>The output image produced by this node (always a new file — Z24).</summary>
    public string ImagePath { get; init; } = "";

    /// <summary>
    /// The node's image pack (Step 9C.10, non-empty for a valid node): the root node carries
    /// the imported image(s), an edit node carries its single output (<see cref="ImagePath"/>
    /// is always <c>ImagePaths[0]</c>). Normalized by <c>EditSession</c> at insertion.
    /// </summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The ordered pipeline images this edit consumed — <c>image1</c>, <c>image2</c>, … with
    /// the main image first (Step 9C.10); empty for the root node. Persisted so a re-run can
    /// reproduce the <c>&lt;imageN&gt;</c> mapping.
    /// </summary>
    public IReadOnlyList<string> UsedImagePaths { get; init; } = Array.Empty<string>();

    /// <summary>The user input that produced this node (shown in the history list).</summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// The node's intrinsic crop (Step 9C.6-B); <c>null</c> when uncropped. At most one
    /// per node — re-adjusting replaces it, never appends a node.
    /// </summary>
    public CropSpec? Crop { get; init; }

    /// <summary>
    /// The node's hand-drawn mask (Step 9C.7); <c>null</c> when unmasked. At most one per
    /// node — re-drawing replaces it, never appends a node. Cleared when the crop changes.
    /// </summary>
    public MaskSpec? Mask { get; init; }

    /// <summary>
    /// The node's re-run snapshot (Step 9C.8-A); <c>null</c> when the edit carried neither
    /// a UI resolution nor reference images. At most one per node — replaced in place.
    /// </summary>
    public RerunSpec? Rerun { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>End-to-end execution time (milliseconds); <c>null</c> when unknown (Step 9C.21).</summary>
    public int? DurationMs { get; init; }
}
