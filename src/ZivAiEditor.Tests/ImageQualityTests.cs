using SkiaSharp;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Deterministic, GPU-free test of the picture-validity assertion itself:
/// a synthetic noise image must be flagged, a smooth structured image must not.
/// </summary>
public class ImageQualityTests
{
    [Fact]
    public void Noise_Image_Is_Flagged_As_Likely_Noise()
    {
        var bytes = EncodePng(256, 256, (x, y, rng) => rng.NextDouble());
        var quality = ImageQuality.Analyze(bytes);
        Assert.True(quality.IsLikelyNoise, $"random noise should be flagged: {quality.Describe()}");
    }

    [Fact]
    public void Structured_Image_Is_Not_Flagged()
    {
        var bytes = EncodePng(256, 256, (x, y, _) =>
        {
            var v = 0.5
                + 0.35 * Math.Sin(x / 18.0)
                + 0.30 * Math.Cos(y / 23.0);
            if (x > 80 && x < 170 && y > 60 && y < 190)
            {
                v -= 0.25;
            }

            return Math.Clamp(v, 0.0, 1.0);
        });
        var quality = ImageQuality.Analyze(bytes);
        Assert.False(quality.IsLikelyNoise, $"a smooth structured image must pass: {quality.Describe()}");
    }

    private static byte[] EncodePng(int width, int height, Func<int, int, Random, double> sample)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        var span = bitmap.GetPixelSpan();
        var rng = new Random(1234);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)Math.Round(Math.Clamp(sample(x, y, rng), 0.0, 1.0) * 255.0);
                var offset = (y * width + x) * 4;
                span[offset] = value;
                span[offset + 1] = value;
                span[offset + 2] = value;
                span[offset + 3] = 255;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
