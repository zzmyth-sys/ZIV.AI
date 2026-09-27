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
/// Headless preview smoke (8K fix): loading a large image into the real <see cref="ImagePreview"/>
/// produces a bounded-size render bitmap while the canvas keeps the original source size, and the
/// masked overlays keep working in original coordinates. No GPU / real window (Z29).
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
    public void Large_Image_Loads_Bounded_Bitmap_With_Original_SourceSize()
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

                // The canvas renders at the ORIGINAL source size (the proxy bitmap is stretched
                // over it), so the model / renderer / overlays share one coordinate space.
                Assert.Equal(4000, canvas!.SourceSize.Width, 3);
                Assert.Equal(1000, canvas.SourceSize.Height, 3);
                Assert.NotNull(canvas.Image);
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
