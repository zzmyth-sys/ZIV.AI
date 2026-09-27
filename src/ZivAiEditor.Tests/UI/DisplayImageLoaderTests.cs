using System;
using System.IO;
using Avalonia;
using SkiaSharp;
using Xunit;
using ZivAiEditor.App.Imaging;
using ZivAiEditor.Imaging;
using ZivAiEditor.Tests.UI;

namespace ZivAiEditor.Tests;

/// <summary>
/// Display-image loader tests (8K single-coordinate-space): the loader reports the DISPLAY bitmap
/// size (the UI's only coordinate space, ≤2.5K) plus the ORIGINAL size for the badge / crop scale,
/// and a saved-project sibling proxy wins over a re-decode. Runs on the headless Avalonia platform
/// (Bitmap needs it).
/// </summary>
[Collection(DisplayProxyCollection.Name)]
public sealed class DisplayImageLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "zivai-display-" + Guid.NewGuid().ToString("N"));

    public DisplayImageLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        ProxyImageCache.CleanupAll();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string WritePng(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(30, 200, 90));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    [Fact]
    public void Small_Image_Loads_At_Full_Size_With_No_Proxy()
    {
        var path = WritePng(Path.Combine(_dir, "small.png"), 800, 600);

        HeadlessTest.Run(() =>
        {
            var loader = new DisplayImageLoader();
            var display = loader.LoadDisplay(path);

            try
            {
                Assert.Null(display.ProxyPath);
                Assert.Equal(new PixelSize(800, 600), display.DisplayPixelSize);
                Assert.Equal(new PixelSize(800, 600), display.OriginalPixelSize);
            }
            finally
            {
                display.Bitmap.Dispose();
            }
        });
    }

    [Fact]
    public void Large_Image_Loads_As_A_Bounded_Proxy_Display_Size_With_Original_Size()
    {
        var path = WritePng(Path.Combine(_dir, "huge.png"), 4000, 1000);

        HeadlessTest.Run(() =>
        {
            var loader = new DisplayImageLoader();
            var display = loader.LoadDisplay(path);

            try
            {
                Assert.NotNull(display.ProxyPath);
                // The display size is the bounded proxy (4000x1000 -> 2560x640); the original size is
                // the full source, carried for the badge / crop scale.
                Assert.Equal(new PixelSize(2560, 640), display.DisplayPixelSize);
                Assert.Equal(new PixelSize(4000, 1000), display.OriginalPixelSize);
            }
            finally
            {
                display.Bitmap.Dispose();
            }
        });
    }

    [Fact]
    public void Project_Sibling_Proxy_Is_Preferred_Over_Re_Decoding_The_Original()
    {
        var original = WritePng(Path.Combine(_dir, "node1.png"), 4000, 1000);
        var sibling = Path.Combine(_dir, "node1_proxy.png");
        Assert.True(ProxyImageCache.SaveProjectProxy(original, IDisplayImageLoader.MaxDisplaySide, sibling));

        HeadlessTest.Run(() =>
        {
            var loader = new DisplayImageLoader();
            var display = loader.LoadDisplay(original);

            try
            {
                Assert.Equal(sibling, display.ProxyPath);
                // Both sizes are read from headers: display = proxy, original = source.
                Assert.Equal(new PixelSize(2560, 640), display.DisplayPixelSize);
                Assert.Equal(new PixelSize(4000, 1000), display.OriginalPixelSize);
            }
            finally
            {
                display.Bitmap.Dispose();
            }
        });
    }
}
