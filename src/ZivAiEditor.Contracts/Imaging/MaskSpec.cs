namespace ZivAiEditor.Contracts.Imaging;

public sealed class MaskSpec
{
    public string MaskImagePath { get; init; } = "";

    /// <summary>
    /// The mask PNG's actual pixel width — i.e. the hand-drawn buffer / <b>display</b> size
    /// (≤2.5K when the pipeline image is a downscaled proxy; equal to the original size for a small
    /// image). Revised from "main image's original pixels" (Z-8K single-coordinate-space change; see
    /// FROZEN tail). Independent of UI zoom / pan; not a request target, preset nor backend output
    /// size. The backend resizes the PNG to the generation target regardless, so it never reads this.
    /// </summary>
    public int Width { get; init; }

    /// <summary>The mask PNG's actual pixel height (see <see cref="Width"/>).</summary>
    public int Height { get; init; }

    public bool IsBinary { get; init; } = true;
    public bool Invert { get; init; }

    /// <summary>
    /// User-explicit feather radius in the mask PNG / buffer's own pixels (<b>buffer pixels</b>,
    /// i.e. the same ≤2.5K display space as <see cref="Width"/> / <see cref="Height"/>; revised from
    /// "original image pixels" — see FROZEN tail). <c>0</c> (default) means a hard 0 / 255 mask. The
    /// exported PNG is grayscale when positive (revised Z19); the editor never auto-dilates /
    /// smart-fills / morphs the mask. Persisted in <c>session.json</c> as <c>feather_px</c>
    /// (append-only; absent in older files → default <c>0</c>). Not an IPC field.
    /// </summary>
    public int FeatherPx { get; init; }
}
