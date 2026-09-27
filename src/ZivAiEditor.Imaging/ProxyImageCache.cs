using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using ZIV.Imaging.Codecs.Skia;

namespace ZivAiEditor.Imaging;

/// <summary>
/// Disk-backed downscaled <b>display proxies</b> for very large images (8K fix). A proxy caps the
/// long side at <see cref="DefaultMaxSide"/>, so the Avalonia renderer never uploads a full-size
/// (100–300 MB) GPU texture and a reopened project never re-decodes the original 8K image.
///
/// <para><b>Content-addressed</b> (Z12): the file name is
/// <c>sha256(fullPath + lastWriteTicks + maxSide).png</c> under
/// <c>{BaseDirectory}/_cache/proxies/</c>, so a source edit changes the mtime and the old proxy is
/// simply no longer looked up (and later evicted). The directory is bounded to
/// <see cref="MaxTotalBytes"/> (500 MB); over the cap the oldest files (by last write) are
/// deleted. An image whose long side already fits is returned unchanged (pass-through, no file).</para>
///
/// <para>Pure BCL + SkiaSharp + ZIV.Imaging; runs off the caller's thread (Z11). Never throws.</para>
/// </summary>
public static class ProxyImageCache
{
    /// <summary>Long-side cap for a display proxy (2.5K). The user-decided threshold.</summary>
    public const int DefaultMaxSide = 2560;

    /// <summary>Total on-disk budget for the runtime proxy cache (500 MB, Z12).</summary>
    private const long MaxTotalBytes = 500L * 1024 * 1024;

    private const string CacheFolderName = "_cache";
    private const string ProxiesFolderName = "proxies";
    private const string PngExtension = ".png";

    /// <summary>The program-directory root of the runtime proxy files.</summary>
    public static string ProxiesRootDirectory
        => Path.Combine(AppContext.BaseDirectory, CacheFolderName, ProxiesFolderName);

    /// <summary>
    /// Reads an image's store-independent pixel size without decoding its pixels (Skia codec
    /// header only). Returns <c>(0, 0)</c> for a missing / unreadable file.
    /// </summary>
    public static (int Width, int Height) ReadPixelSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return (0, 0);
        }

        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return (0, 0);
            }

            return (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[proxy] size read failed '{path}': {ex.Message}");
            return (0, 0);
        }
    }

    /// <summary>
    /// Returns a display-ready path for <paramref name="sourcePath"/>: the source itself when its
    /// long side is already ≤ <paramref name="maxSide"/> (pass-through), otherwise a cached proxy
    /// PNG (generating it on first use). Returns <c>null</c> when the source is missing /
    /// undecodable. Never throws.
    /// </summary>
    public static string? TryGetOrCreate(string sourcePath, int maxSide = DefaultMaxSide)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath) || maxSide <= 0)
        {
            return null;
        }

        var (width, height) = ReadPixelSize(sourcePath);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        if (width <= maxSide && height <= maxSide)
        {
            return sourcePath;
        }

        var proxyPath = ResolveProxyPath(sourcePath, maxSide);
        if (File.Exists(proxyPath))
        {
            Touch(proxyPath);
            return proxyPath;
        }

        if (!Generate(sourcePath, maxSide, proxyPath))
        {
            return null;
        }

        EnforceBound();
        return proxyPath;
    }

    /// <summary>
    /// Persists a proxy for <paramref name="sourcePath"/> at <paramref name="destination"/> (used
    /// at project-save time to write <c>sessions/{sid}/{nodeId}_proxy.png</c>). Returns <c>true</c>
    /// only when a proxy file is present at <paramref name="destination"/> afterwards. A source
    /// that already fits is <b>not</b> copied (no proxy is needed). Never throws.
    /// </summary>
    public static bool SaveProjectProxy(string sourcePath, int maxSide, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return false;
        }

        var proxyPath = TryGetOrCreate(sourcePath, maxSide);
        if (proxyPath is null)
        {
            return false;
        }

        // Pass-through (source already fits) or the destination already is the proxy: no copy.
        if (string.Equals(proxyPath, sourcePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(proxyPath, destination, StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(destination);
        }

        try
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(proxyPath, destination, overwrite: true);
            return File.Exists(destination);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[proxy] project copy failed '{destination}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes every runtime proxy file (startup / app-close cleanup). Never throws.
    /// </summary>
    public static void CleanupAll()
    {
        try
        {
            var root = ProxiesRootDirectory;
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(root))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[proxy] delete failed '{file}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[proxy] cleanup-all failed: {ex.Message}");
        }
    }

    private static string ResolveProxyPath(string sourcePath, int maxSide)
    {
        var key = $"{Path.GetFullPath(sourcePath)}|{File.GetLastWriteTimeUtc(sourcePath).Ticks}|{maxSide}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(ProxiesRootDirectory, hash + PngExtension);
    }

    private static bool Generate(string sourcePath, int maxSide, string destination)
    {
        try
        {
            Directory.CreateDirectory(ProxiesRootDirectory);

            using var image = Downscale(sourcePath, maxSide);
            if (image is null)
            {
                return false;
            }

            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null)
            {
                return false;
            }

            // Write to a sibling temp then move: a crash mid-write cannot leave a truncated
            // proxy that a later run would happily read as "cached".
            var temp = destination + ".tmp";
            try
            {
                using (var stream = File.Create(temp))
                {
                    data.SaveTo(stream);
                }

                File.Move(temp, destination, overwrite: true);
            }
            finally
            {
                // A failed encode / move must not leave the temp file behind.
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch (Exception) { }
                }
            }

            return File.Exists(destination);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[proxy] generate failed '{sourcePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Produces a ≤ <paramref name="maxSide"/> image. Uses the shared Skia codec's native
    /// thumbnail scaling first (cheap for JPEG); when the codec cannot scale natively (PNG) it
    /// decodes once and resizes with Skia. Returns the image scaled to fit.
    /// </summary>
    private static SKImage? Downscale(string sourcePath, int maxSide)
    {
        using (var codec = new SkiaCodec())
        {
            var thumb = codec.LoadThumbnail(sourcePath, maxSide);
            if (thumb is not null)
            {
                if (thumb.Width <= maxSide && thumb.Height <= maxSide)
                {
                    // SKImage.FromBitmap copies the pixels, so the intermediate can be released.
                    using (thumb)
                    {
                        using var thumbnail = SKBitmap.FromImage(thumb);
                        return SKImage.FromBitmap(thumbnail);
                    }
                }

                // PNG (and other non-scalable codecs) hand back a full-size image; release it
                // before the explicit decode so two full buffers are never held at once.
                thumb.Dispose();
            }
        }

        // The codec did not reduce enough (or at all): decode once and resize explicitly.
        using var full = SKBitmap.Decode(sourcePath);
        if (full is null || full.Width <= 0 || full.Height <= 0)
        {
            return null;
        }

        var scale = Math.Min((double)maxSide / full.Width, (double)maxSide / full.Height);
        if (scale >= 1)
        {
            return SKImage.FromBitmap(full);
        }

        var targetWidth = Math.Max(1, (int)Math.Round(full.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(full.Height * scale));
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var resized = full.Resize(info, new SKSamplingOptions(SKFilterMode.Linear));
        return resized is null ? null : SKImage.FromBitmap(resized);
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception)
        {
            // Best effort: a failed touch only affects eviction order.
        }
    }

    /// <summary>Deletes the oldest proxies until the runtime cache is within its byte budget.</summary>
    private static void EnforceBound()
    {
        try
        {
            var root = ProxiesRootDirectory;
            if (!Directory.Exists(root))
            {
                return;
            }

            var files = new DirectoryInfo(root).GetFiles();
            long total = files.Sum(f => f.Length);
            if (total <= MaxTotalBytes)
            {
                return;
            }

            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                if (total <= MaxTotalBytes)
                {
                    break;
                }

                try
                {
                    total -= file.Length;
                    file.Delete();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[proxy] evict failed '{file.FullName}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[proxy] bound enforcement failed: {ex.Message}");
        }
    }
}
