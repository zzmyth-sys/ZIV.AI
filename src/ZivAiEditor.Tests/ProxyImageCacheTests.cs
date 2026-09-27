using System;
using System.IO;
using SkiaSharp;
using Xunit;
using ZivAiEditor.Imaging;

namespace ZivAiEditor.Tests;

/// <summary>
/// 8K display-proxy cache tests: pass-through for small images, downscale for large ones,
/// content-addressed caching / invalidation and cleanup. Pure CPU Skia — no GPU (Z29).
/// </summary>
[Collection(DisplayProxyCollection.Name)]
public sealed class ProxyImageCacheTests : IDisposable
{
    private const int MaxSide = 2560;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "zivai-proxy-" + Guid.NewGuid().ToString("N"));

    public ProxyImageCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        ProxyImageCache.CleanupAll();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WritePng(string name, int width, int height)
    {
        var path = Path.Combine(_dir, name);
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(10, 120, 200));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    [Fact]
    public void Small_Image_Passes_Through_Unchanged()
    {
        var source = WritePng("small.png", 800, 600);

        var result = ProxyImageCache.TryGetOrCreate(source, MaxSide);

        Assert.Equal(source, result);
    }

    [Fact]
    public void Large_Image_Is_Downscaled_To_The_Long_Side_Cap()
    {
        var source = WritePng("huge.png", 4000, 1000);

        var proxy = ProxyImageCache.TryGetOrCreate(source, MaxSide);

        Assert.NotNull(proxy);
        Assert.NotEqual(source, proxy);
        Assert.True(File.Exists(proxy));

        using var decoded = SKBitmap.Decode(proxy!);
        Assert.NotNull(decoded);
        Assert.True(Math.Max(decoded!.Width, decoded.Height) <= MaxSide);
        Assert.Equal(2560, decoded.Width); // 4000 -> 2560 (4:1 aspect kept)
        Assert.Equal(640, decoded.Height);
    }

    [Fact]
    public void Repeated_Lookups_Reuse_The_Same_Cached_Proxy()
    {
        var source = WritePng("huge2.png", 3000, 3000);

        var first = ProxyImageCache.TryGetOrCreate(source, MaxSide);
        var second = ProxyImageCache.TryGetOrCreate(source, MaxSide);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Editing_The_Source_Invalidates_The_Cached_Proxy()
    {
        var source = WritePng("edit.png", 3000, 1000);
        var first = ProxyImageCache.TryGetOrCreate(source, MaxSide);

        // A newer mtime changes the content-addressed key -> a different proxy path.
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
        var second = ProxyImageCache.TryGetOrCreate(source, MaxSide);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ReadPixelSize_Reads_Header_Without_Full_Decode()
    {
        var source = WritePng("size.png", 1234, 567);

        Assert.Equal((1234, 567), ProxyImageCache.ReadPixelSize(source));
        Assert.Equal((0, 0), ProxyImageCache.ReadPixelSize(Path.Combine(_dir, "missing.png")));
    }

    [Fact]
    public void CleanupAll_Removes_Runtime_Proxies()
    {
        var source = WritePng("cleanup.png", 4000, 500);
        var proxy = ProxyImageCache.TryGetOrCreate(source, MaxSide);
        Assert.True(File.Exists(proxy));

        ProxyImageCache.CleanupAll();

        Assert.False(File.Exists(proxy));
        // The source is untouched (Z24).
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void SaveProjectProxy_Copies_A_Proxy_To_The_Destination()
    {
        var source = WritePng("project.png", 4000, 500);
        var destination = Path.Combine(_dir, "sessions", "node1_proxy.png");

        var ok = ProxyImageCache.SaveProjectProxy(source, MaxSide, destination);

        Assert.True(ok);
        Assert.True(File.Exists(destination));
        using var decoded = SKBitmap.Decode(destination);
        Assert.True(Math.Max(decoded!.Width, decoded.Height) <= MaxSide);
    }

    [Fact]
    public void SaveProjectProxy_Skips_A_Source_That_Already_Fits()
    {
        var source = WritePng("smallproject.png", 640, 480);
        var destination = Path.Combine(_dir, "sessions", "small_proxy.png");

        var ok = ProxyImageCache.SaveProjectProxy(source, MaxSide, destination);

        Assert.False(ok);
        Assert.False(File.Exists(destination));
    }
}
