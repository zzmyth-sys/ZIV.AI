namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Edit mode for the chat input (Step 9C.6-C): a single source image, or a multi-image
/// reference edit. The multi-image pipeline (<c>AdditionalImages</c> + <c>&lt;imageN&gt;</c>)
/// landed in Step 9C.5-D and the image-pack model in Step 9C.10; this flag drives the
/// send-time validation and the mode toggle.
/// </summary>
public enum ImageEditMode
{
    /// <summary>One source image (the default).</summary>
    Single,

    /// <summary>Multiple images (validation + dialog).</summary>
    Multi,
}