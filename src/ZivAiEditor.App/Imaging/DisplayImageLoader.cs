using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using ZivAiEditor.Imaging;

namespace ZivAiEditor.App.Imaging;

/// <summary>
/// Default <see cref="IDisplayImageLoader"/>: reads the original pixel size from the codec header
/// (no pixel decode), prefers a project sibling proxy (<c>{nodeId}_proxy.png</c>) when present so
/// reopening a saved project never re-decodes an 8K original, otherwise asks
/// <see cref="ProxyImageCache"/> for a runtime proxy. All heavy work runs on the caller's thread;
/// <see cref="LoadDisplayAsync"/> wraps it in <see cref="Task.Run(System.Action)"/> (Z11).
/// </summary>
public sealed class DisplayImageLoader : IDisplayImageLoader
{
    /// <inheritdoc />
    public DisplayImage LoadDisplay(string path, int maxSide = IDisplayImageLoader.MaxDisplaySide)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var (width, height) = ProxyImageCache.ReadPixelSize(path);
        if (width <= 0 || height <= 0)
        {
            // The original's header is unreadable; fall back to a project sibling proxy if any,
            // and only then to a raw decode (which will honor the file's own size).
            var sibling = ResolveSiblingProxy(path);
            if (sibling is not null)
            {
                var proxy = new Bitmap(sibling);
                return new DisplayImage(proxy, proxy.PixelSize, sibling);
            }

            var direct = new Bitmap(path);
            return new DisplayImage(direct, direct.PixelSize, null);
        }

        var source = new PixelSize(width, height);
        if (width <= maxSide && height <= maxSide)
        {
            var small = new Bitmap(path);
            return new DisplayImage(small, source, null);
        }

        // Large: a saved-project sibling proxy avoids re-decoding the original 8K image.
        var projectProxy = ResolveSiblingProxy(path);
        if (projectProxy is not null)
        {
            var bitmap = new Bitmap(projectProxy);
            return new DisplayImage(bitmap, source, projectProxy);
        }

        var runtimeProxy = ProxyImageCache.TryGetOrCreate(path, maxSide);
        if (runtimeProxy is not null && !string.Equals(runtimeProxy, path, StringComparison.OrdinalIgnoreCase))
        {
            var bitmap = new Bitmap(runtimeProxy);
            return new DisplayImage(bitmap, source, runtimeProxy);
        }

        // Proxy generation failed: decode the source so the caller still gets an image.
        var fallback = new Bitmap(path);
        return new DisplayImage(fallback, source, null);
    }

    /// <inheritdoc />
    public Task<DisplayImage?> LoadDisplayAsync(
        string path,
        int maxSide = IDisplayImageLoader.MaxDisplaySide,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Task.FromResult<DisplayImage?>(null);
        }

        return Task.Run<DisplayImage?>(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                return LoadDisplay(path, maxSide);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[display] load failed '{path}': {ex.Message}");
                return null;
            }
        }, ct);
    }

    /// <summary>
    /// Returns the path of a project sibling proxy (<c>{baseName}_proxy.png</c> next to the
    /// original) when it exists, else <c>null</c>. Only <c>.png</c> sources are considered — the
    /// project save copies every node image as PNG and writes the proxy beside it.
    /// </summary>
    private static string? ResolveSiblingProxy(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "_proxy.png");
        return File.Exists(candidate) ? candidate : null;
    }
}
