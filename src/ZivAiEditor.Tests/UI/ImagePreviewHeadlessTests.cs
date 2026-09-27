using System;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using SkiaSharp;
using Xunit;
using ZivAiEditor.App.Controls;
using ZivAiEditor.App.Imaging;
using ZivAiEditor.Imaging;
using ZivAiEditor.Tests.UI;

namespace ZivAiEditor.Tests;

/// <summary>
/// Headless preview smoke (8K single-coordinate-space): loading a large image into the real
/// <see cref="ImagePreview"/> produces a bounded render bitmap, the canvas / model use that DISPLAY
/// size as their one coordinate space, and the mask buffer is likewise bounded to the display size.
/// No GPU / real window (Z29).
/// </summary>
[Collection(DisplayProxyCollection.Name)]
public sealed class ImagePreviewHeadlessTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "zivai-preview-" + Guid.NewGuid().ToString("N"));

    public ImagePreviewHeadlessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        ProxyImageCache.CleanupAll();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WritePng(string name, int width, int height)
    {
        var path = Path.Combine(_dir, name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(200, 60, 60));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static bool PumpUntil(Func<bool> condition, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }

    [Fact]
    public void Large_Image_Uses_Display_Size_As_The_Single_Coordinate_Space()
    {
        var path = WritePng("big.png", 4000, 1000);

        HeadlessTest.Run(() =>
        {
            var preview = new ImagePreview(new ImagingService(), new DisplayImageLoader());
            try
            {
                var canvas = preview.FindControl<PanZoomCanvas>("PART_ImageCanvas");
                Assert.NotNull(canvas);

                preview.LoadNode(null, null, path, null);

                Assert.True(PumpUntil(() => canvas!.Image is not null), "preview did not finish loading");

                // The canvas draws the proxy at its OWN (display) size — no stretching over an
                // original-size rect. 4000x1000 -> 2560x640.
                Assert.Equal(2560, canvas!.SourceSize.Width, 3);
                Assert.Equal(640, canvas.SourceSize.Height, 3);
                Assert.NotNull(canvas.Image);
            }
            finally
            {
                preview.Close();
            }
        });
    }

    [Fact]
    public void Large_Image_Mask_Buffer_Is_Bounded_To_Display_Size()
    {
        var path = WritePng("maskbig.png", 4000, 1000);

        HeadlessTest.Run(() =>
        {
            var preview = new ImagePreview(new ImagingService(), new DisplayImageLoader());
            try
            {
                var canvas = preview.FindControl<PanZoomCanvas>("PART_ImageCanvas");
                Assert.NotNull(canvas);

                preview.LoadNode(null, null, path, null);
                Assert.True(PumpUntil(() => canvas!.Image is not null), "preview did not finish loading");

                // Entering the mask tool allocates the buffer at the display size (≤2.5K), not the
                // 8K original; brush / feather are buffer pixels (no display scaling), so a stroke
                // at the default brush is continuous.
                preview.ToolState.SetTool(ZivAiEditor.UI.Editing.ToolMode.MaskBrush);
                Dispatcher.UIThread.RunJobs();

                var mask = preview.Mask;
                Assert.NotNull(mask);
                Assert.True(mask!.Width <= IDisplayImageLoader.MaxDisplaySide, $"buffer W {mask.Width}");
                Assert.True(mask.Height <= IDisplayImageLoader.MaxDisplaySide, $"buffer H {mask.Height}");
                Assert.Equal(2560, mask.Width);
                Assert.Equal(40, mask.BrushDiameter);

                // A drag must leave an unbroken band (buffer-pixel diameter used directly).
                var centre = mask.Height / 2;
                mask.BeginStroke(20, centre, erase: false);
                mask.ContinueStroke(mask.Width - 20, centre);
                mask.EndStroke();
                var pixels = mask.CopyPixels();
                for (var x = 20; x <= mask.Width - 20; x++)
                {
                    Assert.Equal(255, pixels[centre * mask.Width + x]);
                }
            }
            finally
            {
                preview.Close();
            }
        });
    }

    [Fact]
    public void Empty_Path_Shows_Empty_State()
    {
        HeadlessTest.Run(() =>
        {
            var preview = new ImagePreview(new ImagingService(), new DisplayImageLoader());
            try
            {
                preview.LoadNode(null, null, Path.Combine(_dir, "missing.png"), null);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(preview.FindControl<PanZoomCanvas>("PART_ImageCanvas")!.Image);
            }
            finally
            {
                preview.Close();
            }
        });
    }
}
