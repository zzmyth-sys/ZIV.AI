using Xunit;
using ZivAiEditor.Imaging;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.7-B pure feather tests: the 3-pass separable box blur, radius-0 identity, buffer
/// validation, clamping and grayscale output. No UI / GPU.
/// </summary>
public class MaskFeatherTests
{
    private static byte[] Block(int width, int height, int x0, int y0, int x1, int y1)
    {
        var pixels = new byte[width * height];
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                pixels[y * width + x] = 255;
            }
        }

        return pixels;
    }

    [Fact]
    public void Radius_Zero_Returns_Byte_Identical_Clone()
    {
        var hard = Block(16, 16, 4, 4, 12, 12);

        var result = MaskFeather.Apply(hard, 16, 16, 0);

        Assert.NotSame(hard, result);
        Assert.Equal(hard, result);
        Assert.All(result, value => Assert.True(value is 0 or 255));
    }

    [Fact]
    public void Positive_Radius_Produces_Mid_Values()
    {
        var hard = Block(32, 32, 12, 12, 20, 20);

        var result = MaskFeather.Apply(hard, 32, 32, 4);

        Assert.Contains(result, value => value is > 0 and < 255);
        Assert.All(result, value => Assert.InRange(value, (byte)0, (byte)255));
    }

    [Fact]
    public void Output_Is_Deterministic()
    {
        var hard = Block(24, 20, 6, 5, 18, 15);

        var first = MaskFeather.Apply(hard, 24, 20, 6);
        var second = MaskFeather.Apply(hard, 24, 20, 6);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Radius_Is_Clamped_Above_Max()
    {
        var hard = Block(40, 40, 10, 10, 30, 30);

        var atMax = MaskFeather.Apply(hard, 40, 40, MaskFeather.MaxRadiusPx);
        var aboveMax = MaskFeather.Apply(hard, 40, 40, MaskFeather.MaxRadiusPx + 100);

        Assert.Equal(atMax, aboveMax);
    }

    [Fact]
    public void Negative_Radius_Behaves_As_Zero()
    {
        var hard = Block(16, 16, 4, 4, 12, 12);

        var result = MaskFeather.Apply(hard, 16, 16, -5);

        Assert.Equal(hard, result);
    }

    [Fact]
    public void Short_Buffer_Throws_ArgumentException()
    {
        var tooShort = new byte[10];

        Assert.Throws<ArgumentException>(() => MaskFeather.Apply(tooShort, 4, 4, 3));
    }

    [Fact]
    public void Null_Buffer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MaskFeather.Apply(null!, 4, 4, 3));
    }

    [Fact]
    public void Blur_Preserves_Mass_Roughly()
    {
        // A box blur is a weighted average, so the total (sum) stays close to the source.
        var hard = Block(30, 30, 10, 10, 20, 20);
        long sourceSum = 0;
        foreach (var value in hard)
        {
            sourceSum += value;
        }

        var result = MaskFeather.Apply(hard, 30, 30, 3);
        long resultSum = 0;
        foreach (var value in result)
        {
            resultSum += value;
        }

        // Edge clamping can lose a little, but the totals must be in the same ballpark.
        Assert.True(Math.Abs(resultSum - sourceSum) < sourceSum * 0.25);
    }
}