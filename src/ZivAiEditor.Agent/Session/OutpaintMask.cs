using ZivAiEditor.Contracts.Session;

namespace ZivAiEditor.Agent.Session;

/// <summary>
/// Pure, dependency-free outpaint mask geometry (P1 · <c>/扩图</c> relocation). Mirrors the
/// Python backend's <c>python/server/outpaint.py</c> <c>build_mask</c> / <c>_dilate</c>: the
/// new (gray) canvas region is 1, ramping to 0 over <c>feathering</c> px into the source
/// (squared ramp), then grown by <c>grow</c> px and blurred by three box passes. It is a
/// <c>byte[]</c>-only raster op (255 = gray / new, 0 = source), so the Agent stays free of
/// any imaging / GPU dependency.
///
/// <para>The crop rectangle may reach outside the source image (a crop-tool outpaint); only
/// the visible canvas intersection is written. When the source size is unknown the whole
/// canvas is returned as 255 so the caller can still regenerate the gray area.</para>
/// </summary>
public static class OutpaintMask
{
    /// <summary>Feather ramp width into the source, matching the official blueprint (px).</summary>
    private const float Feathering = 40f;

    /// <summary>Rectangular dilation radius for the new region (px).</summary>
    private const int Grow = 20;

    /// <summary>Box-blur radius per pass; three passes approximate PIL <c>GaussianBlur(31)</c>.</summary>
    private const int BlurRadius = 31;

    /// <summary>Number of separable box-blur passes.</summary>
    private const int BlurPasses = 3;

    /// <summary>
    /// Builds a <paramref name="canvasW"/>×<paramref name="canvasH"/> row-major mask for the
    /// crop's outpaint region: <c>255</c> = gray / new region to regenerate, <c>0</c> = source.
    /// Returns all-<c>255</c> when the source size is unknown or the canvas is empty.
    /// </summary>
    public static byte[] Generate(CropSpec crop, int canvasW, int canvasH)
    {
        var size = Math.Max(0, (long)canvasW * canvasH);
        var output = new byte[size];

        if (crop.SourceWidth <= 0 || crop.SourceHeight <= 0 || canvasW < 1 || canvasH < 1)
        {
            for (var i = 0L; i < size; i++)
            {
                output[i] = 255;
            }

            return output;
        }

        var mask = new float[size];
        Array.Fill(mask, 1f);

        var px = -crop.X;
        var py = -crop.Y;
        var sw = crop.SourceWidth;
        var sh = crop.SourceHeight;

        var topPad = py > 0;
        var bottomPad = py + sh < canvasH;
        var leftPad = px > 0;
        var rightPad = px + sw < canvasW;

        var patch = new float[(long)sh * sw];

        // Official ImagePadForOutpaint only feathers when the source is larger than twice the
        // feather width on both axes (nodes.py:2044). Otherwise the whole source stays 0 — a
        // hard boundary — so a small source is not ramped.
        if (Feathering * 2f < sw && Feathering * 2f < sh)
        {
            for (var i = 0; i < sh; i++)
            {
                var dt = topPad ? i : sh;
                var db = bottomPad ? sh - i : sh;
                var rowBase = (long)i * sw;
                for (var j = 0; j < sw; j++)
                {
                    var dl = leftPad ? j : sw;
                    var dr = rightPad ? sw - j : sw;
                    var d = Math.Min(Math.Min(dt, db), Math.Min(dl, dr));
                    var v = (Feathering - d) / Feathering;
                    v = v < 0f ? 0f : v > 1f ? 1f : v;
                    patch[rowBase + j] = v * v;
                }
            }
        }

        var mx0 = Math.Max(0, px);
        var my0 = Math.Max(0, py);
        var mx1 = Math.Min(canvasW, px + sw);
        var my1 = Math.Min(canvasH, py + sh);
        if (mx1 > mx0 && my1 > my0)
        {
            var sx0 = mx0 - px;
            var sy0 = my0 - py;
            var copyW = mx1 - mx0;
            for (var row = 0; row < my1 - my0; row++)
            {
                Array.Copy(
                    patch, (long)(sy0 + row) * sw + sx0,
                    mask, (long)(my0 + row) * canvasW + mx0,
                    copyW);
            }
        }

        if (Grow > 0)
        {
            Dilate(mask, canvasW, canvasH, Grow);
        }

        BoxBlur(mask, canvasW, canvasH, BlurRadius);

        for (var i = 0L; i < size; i++)
        {
            var v = mask[i];
            v = v < 0f ? 0f : v > 1f ? 1f : v;
            output[i] = (byte)Math.Round(v * 255.0);
        }

        return output;
    }

    /// <summary>
    /// Separable rectangular max-dilation, port of the Python <c>_dilate</c>: grows the
    /// 1.0-region by <paramref name="radius"/> px in both axes (a (2r+1)² max window).
    /// </summary>
    private static void Dilate(float[] mask, int width, int height, int radius)
    {
        var horizontal = new float[mask.Length];
        Array.Copy(mask, horizontal, mask.Length);

        for (var shift = 1; shift <= radius; shift++)
        {
            for (var y = 0; y < height; y++)
            {
                var row = (long)y * width;
                for (var x = shift; x < width; x++)
                {
                    var v = mask[row + x - shift];
                    if (v > horizontal[row + x])
                    {
                        horizontal[row + x] = v;
                    }
                }

                for (var x = 0; x + shift < width; x++)
                {
                    var v = mask[row + x + shift];
                    if (v > horizontal[row + x])
                    {
                        horizontal[row + x] = v;
                    }
                }
            }
        }

        Array.Copy(horizontal, mask, mask.Length);

        for (var shift = 1; shift <= radius; shift++)
        {
            for (var y = shift; y < height; y++)
            {
                var row = (long)y * width;
                var prev = (long)(y - shift) * width;
                for (var x = 0; x < width; x++)
                {
                    var v = horizontal[prev + x];
                    if (v > mask[row + x])
                    {
                        mask[row + x] = v;
                    }
                }
            }

            for (var y = 0; y + shift < height; y++)
            {
                var row = (long)y * width;
                var next = (long)(y + shift) * width;
                for (var x = 0; x < width; x++)
                {
                    var v = horizontal[next + x];
                    if (v > mask[row + x])
                    {
                        mask[row + x] = v;
                    }
                }
            }
        }
    }

    /// <summary>Three separable box-blur passes (edge-clamped), O(N) per pass.</summary>
    private static void BoxBlur(float[] mask, int width, int height, int radius)
    {
        if (radius <= 0 || width < 1 || height < 1)
        {
            return;
        }

        var temp = new float[mask.Length];
        for (var pass = 0; pass < BlurPasses; pass++)
        {
            BlurHorizontal(mask, temp, width, height, radius);
            BlurVertical(temp, mask, width, height, radius);
        }
    }

    private static void BlurHorizontal(float[] src, float[] dst, int width, int height, int radius)
    {
        var prefix = new float[width + 1];
        for (var y = 0; y < height; y++)
        {
            var row = (long)y * width;
            prefix[0] = 0f;
            for (var x = 0; x < width; x++)
            {
                prefix[x + 1] = prefix[x] + src[row + x];
            }

            for (var x = 0; x < width; x++)
            {
                var lo = Math.Max(0, x - radius);
                var hi = Math.Min(width - 1, x + radius);
                dst[row + x] = (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
            }
        }
    }

    private static void BlurVertical(float[] src, float[] dst, int width, int height, int radius)
    {
        var prefix = new float[height + 1];
        for (var x = 0; x < width; x++)
        {
            prefix[0] = 0f;
            for (var y = 0; y < height; y++)
            {
                prefix[y + 1] = prefix[y] + src[(long)y * width + x];
            }

            for (var y = 0; y < height; y++)
            {
                var lo = Math.Max(0, y - radius);
                var hi = Math.Min(height - 1, y + radius);
                dst[(long)y * width + x] = (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
            }
        }
    }
}
