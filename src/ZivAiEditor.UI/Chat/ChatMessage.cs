using System;
using System.Collections.Generic;
using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.UI.Chat;

/// <summary>Who produced a chat message.</summary>
public enum ChatRole
{
    User,
    Assistant,
    System,
}

/// <summary>One rendered line in the chat stream (INTERACTION.md §3).</summary>
public sealed class ChatMessage
{
    public ChatRole Role { get; init; }

    public string Text { get; init; } = "";

    /// <summary>Optional preview image (a user input or an edit output — Z24 new file).</summary>
    public string? ImagePath { get; init; }

    /// <summary>
    /// The full display pack for an image bubble (Step 9C.10): the "起始图像" bubble carries
    /// the root's whole pack (pipeline main first); an edit bubble carries its single output.
    /// Empty when the bubble has no image; the App falls back to <see cref="ImagePath"/>.
    /// </summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The node's hand-drawn mask (E1): when set, the App overlays it (<b>semi-transparent
    /// red</b>, feathered) on the bubble's image. A display hint only — the mask itself stays
    /// a node property / file. <c>null</c> when the bubble's node has no mask.
    /// </summary>
    public string? MaskPath { get; init; }

    /// <summary>
    /// Feather radius (image pixels) used to render <see cref="MaskPath"/> as a soft overlay
    /// in the bubble. <c>0</c> = hard 0 / 255 edge.
    /// </summary>
    public int MaskFeatherPx { get; init; }

    public bool IsError { get; init; }

    /// <summary>
    /// True for the in-flight "生成中…" bubble. The App layer renders live
    /// preview frames (0x02 IPC frames) into this bubble while the executor runs.
    /// </summary>
    public bool IsPending { get; init; }

    /// <summary>
    /// The session node this message belongs to (assistant bubbles), so a re-run can
    /// update the bubble <b>in place</b> instead of appending a new one (Step 9C.8-A3).
    /// <c>null</c> for user / system bubbles and for a not-yet-appended pending bubble.
    /// </summary>
    public string? NodeId { get; init; }
}

/// <summary>One entry in the history list, carrying its tree depth for indentation.</summary>
public sealed class HistoryItem
{
    public IEditNode Node { get; init; } = null!;

    public int Depth { get; init; }

    public bool IsCurrent { get; init; }
}
