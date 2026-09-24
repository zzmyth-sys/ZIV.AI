using System;

namespace ZivAiEditor.Imaging;

/// <summary>
/// Pure feathering for the hand-drawn mask (Step 9C.7-B). Turns the hard 0 / 255 buffer
/// into an 8-bit grayscale mask by running a <b>3-pass separable box blur</b> (a cheap,
/// O(width·height) Gaussian approximation), so the same code feeds both the on-screen
/// overlay and the exported PNG. It carries <b>no</b> Avalonia / Skia dependency (Z3/Z6),
/// so it is unit-testable without a UI thread or GPU.
///
/// <para><b>Kernel mapping.</b> A box blur is separable and so is repeated application:
/// three box passes of radius <c>r</c> approximate a Gaussian of standard deviation
/// <c>σ ≈ r·sqrt(3·(2r+1)² / 12)</c>. To make a requested feather <c>radiusPx</c> (px)
/// behave like that Gaussian, each pass uses the same integer box half-width
/// <c>r = clamp(round(radiusPx / 2), 1, 25)</c>; the mapping is deterministic (no
/// randomness), independent of image size, and documented here so display and export
/// stay identical.</para>
///
/// <para><b>Z19 revised.</b> This is the only place the editor turns hard pixels into gray
/// values; the live buffer stays 0 / 255 and no auto-dilation / smart-fill / morphing is
/// performed.</para>
///
/// <para>Module-boundary migration step 4: the class is <b>internal</b> to the imaging domain;
/// the UI reaches feathering only through <see cref="IImagingService.FeatherMask"/>.</para>
/// </summary>
internal static class MaskFeather
{
    /// <summary>
    /// Algorithmic upper bound on the feather radius, in pixels; this pure function supports
    /// up to 25px. The <b>product</b> UI caps the user-facing value at 15px
    /// (<c>MaskState.MaxFeatherPx</c>, E8), so the two values intentionally differ — keep this
    /// at the algorithm's capability and do not raise the UI cap to match (M-9).
    /// </summary>
    public const int MaxRadiusPx = 25;

    /// <summary>
    /// Returns a new buffer where <paramref name="hard"/> (0 / 255, row-major,
    /// <paramref name="width"/>×<paramref name="height"/>) is bloomed into a soft
    /// grayscale ramp. <paramref name="radiusPx"/> is clamped to <c>[0, 25]</c>; a radius
    /// of 0 returns a byte-identical clone (still only 0 / 255).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="hard"/> is shorter than <c>width*height</c>.
    /// </exception>
    public static byte[] Apply(byte[] hard, int width, int height, int radiusPx)
    {
        if (hard is null)
        {
            throw new ArgumentNullException(nameof(hard));
        }

        if (width <= 0 || height <= 0 || hard.Length < width * height)
        {
            throw new ArgumentException("mask buffer must cover width*height", nameof(hard));
        }

        var radius = Math.Clamp(radiusPx, 0, MaxRadiusPx);
        var result = new byte[width * height];
        Array.Copy(hard, result, width * height);

        // radius 0 keeps the hard 0 / 255 output (only a clone differs from the input).
        if (radius == 0)
        {
            return result;
        }

        // Each of the three passes is a horizontal + vertical 1-D box blur, so the blur is
        // separable: a requested feather radius maps to one integer box half-width, and the
        // same value is used for every pass (deterministic, image-size independent).
        var box = Math.Clamp((int)Math.Round(radius / 2.0), 1, MaxRadiusPx);
        var scratch = new byte[result.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(result, scratch, width, height, box);
            BoxBlurVertical(scratch, result, width, height, box);
        }

        return result;
    }

    /// <summary>One horizontal box blur with a sliding window sum (O(n) per row).</summary>
    private static void BoxBlurHorizontal(byte[] source, byte[] destination, int width, int height, int box)
    {
        var window = box * 2 + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var sum = 0;
            for (var x = -box; x <= box; x++)
            {
                sum += source[row + Math.Clamp(x, 0, width - 1)];
            }

            for (var x = 0; x < width; x++)
            {
                destination[row + x] = (byte)((sum + window / 2) / window);
                var outgoing = source[row + Math.Clamp(x - box, 0, width - 1)];
                var incoming = source[row + Math.Clamp(x + box + 1, 0, width - 1)];
                sum += incoming - outgoing;
            }
        }
    }

    /// <summary>One vertical box blur with a sliding window sum (O(n) per column).</summary>
    private static void BoxBlurVertical(byte[] source, byte[] destination, int width, int height, int box)
    {
        var window = box * 2 + 1;
        for (var x = 0; x < width; x++)
        {
            var sum = 0;
            for (var y = -box; y <= box; y++)
            {
                sum += source[Math.Clamp(y, 0, height - 1) * width + x];
            }

            for (var y = 0; y < height; y++)
            {
                destination[y * width + x] = (byte)((sum + window / 2) / window);
                var outgoing = source[Math.Clamp(y - box, 0, height - 1) * width + x];
                var incoming = source[Math.Clamp(y + box + 1, 0, height - 1) * width + x];
                sum += incoming - outgoing;
            }
        }
    }
}
