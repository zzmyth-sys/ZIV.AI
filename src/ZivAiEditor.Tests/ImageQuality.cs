using SkiaSharp;

namespace ZivAiEditor.Tests;

/// <summary>
/// Cheap structural sanity check for generated PNGs (Step 4).
///
/// A step-4 regression produced pure-noise outputs that still passed the
/// size/existence assertions. Global variance alone does not separate the two
/// on this model (measured: valid edit variance 0.007-0.075, broken-TE noise
/// 0.007-0.037), so this uses two structure metrics that did separate them:
///
///  - <see cref="BlockRatio"/>: variance of 8x8 block means / global variance.
///    Measured valid 0.85-0.97 vs noise 0.30-0.74. Noise averages out over a
///    block, so it has little large-scale structure.
///  - <see cref="Lag1"/>: horizontal lag-1 autocorrelation. Measured valid
///    0.95-0.99 vs noise 0.86-0.96 (weaker, used as a secondary guard).
/// </summary>
public sealed class ImageQualityResult
{
    public const double BlockRatioMin = 0.80;
    public const double Lag1Min = 0.90;

    public int Width { get; init; }
    public int Height { get; init; }
    public double Variance { get; init; }
    public double BlockRatio { get; init; }
    public double Lag1 { get; init; }

    /// <summary>True when the image lacks large-scale structure (likely noise).</summary>
    public bool IsLikelyNoise => BlockRatio < BlockRatioMin || Lag1 < Lag1Min;

    public string Describe()
        => $"size={Width}x{Height} var={Variance:F4} blockRatio={BlockRatio:F3} lag1={Lag1:F3} "
           + $"(min blockRatio={BlockRatioMin}, min lag1={Lag1Min})";
}

internal static class ImageQuality
{
    public static ImageQualityResult Analyze(byte[] pngBytes)
    {
        using var bitmap = SKBitmap.Decode(pngBytes)
            ?? throw new InvalidOperationException("could not decode PNG");
        using var rgba = bitmap.Copy(SKColorType.Rgba8888);
        return AnalyzeRgba(rgba.Width, rgba.Height, rgba.GetPixelSpan());
    }

    internal static ImageQualityResult AnalyzeRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var count = width * height;
        var luma = new double[count];
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            var offset = i * 4;
            var value = (0.299 * rgba[offset] + 0.587 * rgba[offset + 1] + 0.114 * rgba[offset + 2]) / 255.0;
            luma[i] = value;
            sum += value;
        }

        var mean = sum / count;
        var variance = 0.0;
        for (var i = 0; i < count; i++)
        {
            var d = luma[i] - mean;
            variance += d * d;
        }

        variance /= count;
        if (variance <= 1e-9)
        {
            return new ImageQualityResult { Width = width, Height = height, Variance = variance };
        }

        var blockRatio = BlockMeanVarianceRatio(luma, width, height, mean, variance);
        var lag1 = Lag1Autocorrelation(luma, width, height, mean, variance);
        return new ImageQualityResult
        {
            Width = width,
            Height = height,
            Variance = variance,
            BlockRatio = blockRatio,
            Lag1 = lag1,
        };
    }

    private static double BlockMeanVarianceRatio(double[] luma, int width, int height, double mean, double variance)
    {
        const int block = 8;
        var blocksX = width / block;
        var blocksY = height / block;
        if (blocksX == 0 || blocksY == 0)
        {
            return 1.0;
        }

        var blockMeans = new double[blocksX * blocksY];
        var index = 0;
        var blockVar = 0.0;
        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var acc = 0.0;
                for (var y = 0; y < block; y++)
                {
                    var row = (by * block + y) * width + bx * block;
                    for (var x = 0; x < block; x++)
                    {
                        acc += luma[row + x];
                    }
                }

                var blockMean = acc / (block * block);
                blockMeans[index++] = blockMean;
                var d = blockMean - mean;
                blockVar += d * d;
            }
        }

        blockVar /= blockMeans.Length;
        return blockVar / variance;
    }

    private static double Lag1Autocorrelation(double[] luma, int width, int height, double mean, double variance)
    {
        if (width < 2)
        {
            return 0.0;
        }

        var cov = 0.0;
        var pairs = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width - 1; x++)
            {
                cov += (luma[row + x] - mean) * (luma[row + x + 1] - mean);
                pairs++;
            }
        }

        return cov / (pairs * variance);
    }
}
