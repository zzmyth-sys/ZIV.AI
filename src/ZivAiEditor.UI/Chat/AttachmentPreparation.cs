namespace ZivAiEditor.UI.Chat;

/// <summary>
/// Result of resolving the pending attachment strip against the session before a submit
/// (Step 9C.6-C).
/// </summary>
public enum AttachmentPreparation
{
    /// <summary>Safe to submit; the session has (or was given) an input image.</summary>
    Ready,

    /// <summary>Attachments exist and the session already has a root: the caller must ask
    /// the user (new session / cancel) and then call
    /// <see cref="SessionViewModel.StartNewSessionFrom"/> or abort.</summary>
    NeedsDecision,

    /// <summary>No attachments, no root, and a slash command: block and hint.</summary>
    NoImage,
}