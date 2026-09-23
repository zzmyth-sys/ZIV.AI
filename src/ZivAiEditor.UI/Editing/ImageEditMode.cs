namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Edit mode for the chat input (Step 9C.6-C): a single source image, or a multi-image
/// reference edit. Multi is UI-only for now — the multi-image pipeline
/// (<c>AdditionalImages</c> + <c>&lt;imageN&gt;</c>) is deferred to Step 9C.5-D.
/// </summary>
public enum ImageEditMode
{
    /// <summary>One source image (the default).</summary>
    Single,

    /// <summary>Multiple images (validation + dialog only; pipeline deferred).</summary>
    Multi,
}