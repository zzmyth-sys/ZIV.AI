using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 3 (P1 · <c>/扩图</c> relocation): the pure outpaint mask geometry. Structural only
/// (255 = gray / new region, 0 = source); no GPU / imaging dependency.
/// </summary>
public class OutpaintMaskTests
{
    private static CropSpec Rect(int x, int y, int w, int h, int sourceW, int sourceH)
        => new() { X = x, Y = y, Width = w, Height = h, SourceWidth = sourceW, SourceHeight = sourceH };

    private static byte At(byte[] mask, int width, int x, int y) => mask[y * width + x];

    [Fact]
    public void Generate_Dimensions_Match_Canvas()
    {
        var crop = Rect(-50, -50, 300, 200, 200, 160);

        var mask = OutpaintMask.Generate(crop, 300, 200);

        Assert.Equal(300 * 200, mask.Length);
    }

    [Fact]
    public void Generate_New_Corner_Is_Bright_And_Deep_Source_Is_Dark()
    {
        // Source 400x400 pasted at (50,50) into a 500x500 canvas: an outpaint on all sides.
        var crop = Rect(-50, -50, 500, 500, 400, 400);

        var mask = OutpaintMask.Generate(crop, 500, 500);

        // Top-left corner lies in the gray new region, far from the source edge.
        Assert.True(At(mask, 500, 5, 5) > 250, "new-region corner should stay bright");
        // Deep source center is far from every padded edge / grown region.
        Assert.True(At(mask, 500, 250, 250) < 5, "deep source center should stay dark");
    }

    [Fact]
    public void Generate_Clipped_Source_Keeps_New_Region_And_Dark_Source()
    {
        // X=-50 with Width = SourceWidth-10: the source extends past the right canvas edge.
        var crop = Rect(-50, 0, 390, 400, 400, 400);

        var mask = OutpaintMask.Generate(crop, 390, 400);

        Assert.Equal(390 * 400, mask.Length);
        // Left band is the new gray region.
        Assert.True(At(mask, 390, 5, 200) > 250, "clipped-source new region should stay bright");
        // Right side is deep source, far from the feathered left seam.
        Assert.True(At(mask, 390, 300, 200) < 5, "deep source should stay dark");
    }

    [Fact]
    public void Generate_Small_Source_Skips_Feather_Ramp()
    {
        // Official guard (nodes.py:2044): feather only when the source is larger than
        // 2*feathering (80) on BOTH axes. A source height of 81 passes, 79 does not; both are
        // pasted at y=+30 (a top outpaint), so only the 81-high source carries the 40px ramp.
        var tall = OutpaintMask.Generate(Rect(0, -30, 200, 111, 200, 81), 200, 111);
        var small = OutpaintMask.Generate(Rect(0, -30, 200, 109, 200, 79), 200, 109);

        static double BandMean(byte[] mask, int width, int topRow, int rows)
        {
            long sum = 0;
            for (var y = topRow; y < topRow + rows; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    sum += mask[y * width + x];
                }
            }

            return sum / (double)(rows * width);
        }

        // The source band (canvas rows 30..100) is measurably brighter when the ramp applies.
        var tallMean = BandMean(tall, 200, 30, 70);
        var smallMean = BandMean(small, 200, 30, 70);
        Assert.True(tallMean > smallMean + 1.0, $"tall={tallMean:F2} small={smallMean:F2}");
    }

    [Fact]
    public void Generate_Unknown_Source_Size_Is_All_New()
    {
        var crop = Rect(-50, -50, 120, 80, 0, 0);

        var mask = OutpaintMask.Generate(crop, 120, 80);

        Assert.Equal(120 * 80, mask.Length);
        Assert.All(mask, value => Assert.Equal(255, value));
    }
}
