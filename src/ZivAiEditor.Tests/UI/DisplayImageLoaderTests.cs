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
/// Display-image loader tests (8K fix): the renderer must only ever receive a bounded-size bitmap
/// while the model / overlays keep the ORIGINAL pixel size, and a saved-project sibling proxy must
/// win over a re-decode. Runs on the headless Avalonia platform (Bitmap needs it).
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
                Assert.Equal(new PixelSize(800, 600), display.SourcePixelSize);
            }
            finally
            {
                display.Bitmap.Dispose();
            }
        });
    }

    [Fact]
    public void Large_Image_Loads_As_A_Bounded_Proxy_With_Original_Source_Size()
    {
        var path = WritePng(Path.Combine(_dir, "huge.png"), 4000, 1000);

        HeadlessTest.Run(() =>
        {
            var loader = new DisplayImageLoader();
            var display = loader.LoadDisplay(path);

            try
            {
                // The proxy file is real and bounded (its Skia decode is asserted in
                // ProxyImageCacheTests); the loader must report the ORIGINAL source size so the
                // view-model and overlays keep original coordinates.
                Assert.NotNull(display.ProxyPath);
                Assert.Equal(new PixelSize(4000, 1000), display.SourcePixelSize);
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
                // The source size still comes from the original header, not the proxy.
                Assert.Equal(new PixelSize(4000, 1000), display.SourcePixelSize);
            }
            finally
            {
                display.Bitmap.Dispose();
            }
        });
    }
}
