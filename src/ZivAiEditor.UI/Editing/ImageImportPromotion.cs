namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Pure rule for promoting an imported image batch to the session root (Step 9C.10-P2, R5):
/// an import that takes an empty attachment strip to a non-empty one on a session with no
/// root image promotes the whole batch to the multi-image "原图" pack. The App layer
/// delegates here so the transition is unit-testable without Avalonia.
/// </summary>
public static class ImageImportPromotion
{
    /// <summary>
    /// Whether an import transition should promote the batch to the root. True only for a
    /// removal-free add that goes from empty to non-empty while no root image is set; an
    /// append to a non-empty strip or a removal never promotes.
    /// </summary>
    public static bool ShouldPromote(int countBefore, int countAfter, bool hasRootImage)
        => countBefore == 0 && countAfter > 0 && !hasRootImage;
}
