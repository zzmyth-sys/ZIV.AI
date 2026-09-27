using System;
using System.Linq;
using Xunit;
using ZivAiEditor.App.Controls;
using ZivAiEditor.Tests.UI;

namespace ZivAiEditor.Tests;

/// <summary>
/// Mask-overlay display bitmap tests (8K fix): an oversize mask is downsampled to the long-side
/// cap so it never becomes an 8K texture, while a normal mask is built at full size. Headless only
/// (WriteableBitmap needs the Avalonia platform), no GPU (Z29).
/// </summary>
public sealed class MaskOverlayBitmapTests
{
    [Fact]
    public void Oversize_Mask_Is_Downsampled_To_The_Cap()
    {
        const int width = 8192;
        const int height = 4096;
        var display = new byte[width * height];
        // Painted corner so the buffer is not all zero.
        for (var i = 0; i < 1000; i++)
        {
            display[i] = 255;
        }

        HeadlessTest.Run(() =>
        {
            var bitmap = MaskOverlayBitmap.BuildScaled(display, width, height, 2560, out var outWidth, out var outHeight);
            try
            {
                Assert.NotNull(bitmap);
                Assert.True(Math.Max(outWidth, outHeight) <= 2560);
                Assert.Equal(2560, bitmap!.PixelSize.Width);
                Assert.Equal(1280, bitmap.PixelSize.Height);
            }
            finally
            {
                bitmap?.Dispose();
            }
        });
    }

    [Fact]
    public void Normal_Mask_Is_Built_At_Full_Size()
    {
        const int width = 1024;
        const int height = 768;
        var display = Enumerable.Repeat((byte)255, width * height).ToArray();

        HeadlessTest.Run(() =>
        {
            var bitmap = MaskOverlayBitmap.BuildScaled(display, width, height, 2560, out var outWidth, out var outHeight);
            try
            {
                Assert.NotNull(bitmap);
                Assert.Equal(width, outWidth);
                Assert.Equal(height, outHeight);
                Assert.Equal(width, bitmap!.PixelSize.Width);
            }
            finally
            {
                bitmap?.Dispose();
            }
        });
    }
}
