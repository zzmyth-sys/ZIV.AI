using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using ZIV.Imaging.Codecs.Skia;

namespace ZivAiEditor.Imaging;

/// <summary>
/// Crops (or outpaints) a source image to a pixel rectangle and writes the result as a new
/// PNG (Step 9C.6-B2 / 9C.4-B). It reuses the shared ZIV.Imaging <see cref="SkiaCodec"/> for
/// decode and SkiaSharp directly for the composite (ZIV.Imaging exposes no crop / canvas
/// primitive). The rectangle is in source-image coordinates; a negative X / Y means the
/// source is pasted inside a larger canvas whose remaining area is filled with grey 0.5
/// (matching <c>python/server/outpaint.py</c>). The source file is never modified (Z24).
///
/// <para><b>Temporary area</b> (Z14): the crop is an intermediate product, so it is
/// written under the <b>program directory</b> at
/// <c>_cache/crops/{sessionId}/{nodeId}.png</c> — one file per node, overwritten on
/// re-crop (never accumulated), removed when the session closes / resets and on the next
/// startup. AI result images and user imports are untouched.</para>
///
/// <para>Pure BCL + SkiaSharp + ZIV.Imaging, so it is unit-testable without a UI thread
/// or GPU. Reached through <see cref="IImagingService"/> (module-boundary migration step 4);
/// <c>ZivAiEditor.UI</c> / <c>ZivAiEditor.App</c> do not reference this type directly.</para>
/// </summary>
public static class ImageCropper
{
    private const string CacheFolderName = "_cache";
    private const string CropsFolderName = "crops";
    private const string PngExtension = ".png";

    /// <summary>The program-directory root of all crop temp files (Z14).</summary>
    public static string CropsRootDirectory
        => Path.Combine(AppContext.BaseDirectory, CacheFolderName, CropsFolderName);

    /// <summary>
    /// The crop temp path for one node: <c>_cache/crops/{sessionId}/{nodeId}.png</c>. One
    /// file per node — re-cropping the same node overwrites it.
    /// </summary>
    public static string ResolveCropPath(string sessionId, string nodeId)
        => Path.Combine(CropsRootDirectory, sessionId, nodeId + PngExtension);

    /// <summary>Grey 0.5 outpaint fill — matches <c>outpaint.CANVAS_FILL</c> (D1).</summary>
    private static readonly SKColor CanvasFill = new(128, 128, 128);

    /// <summary>
    /// Crops / outpaints <paramref name="sourceImagePath"/> to the given pixel rectangle and
    /// writes (overwriting) <c>_cache/crops/{sessionId}/{nodeId}.png</c>. X / Y may be
    /// negative (outpaint); the output canvas is <paramref name="width"/>×<paramref name="height"/>
    /// and the source is pasted at <c>(-x, -y)</c>. Returns the output path, or <c>null</c> on
    /// any failure (missing source, blank ids, decode / encode failure, empty rectangle). The
    /// whole operation runs off the caller's thread (Z11).
    /// </summary>
    public static Task<string?> CropAsync(
        string sessionId,
        string nodeId,
        string sourceImagePath,
        int x,
        int y,
        int width,
        int height,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(nodeId)
            || string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath)
            || width <= 0 || height <= 0)
        {
            return Task.FromResult<string?>(null);
        }

        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            using var codec = new SkiaCodec();

            // Full-resolution decode (int.MaxValue = no downscale), matching the preview's
            // raw pixel orientation so the selection rectangle maps 1:1.
            using var full = codec.LoadThumbnail(sourceImagePath, int.MaxValue);
            if (full is null)
            {
                return null;
            }

            // The output canvas is the selection rectangle (may extend beyond the source);
            // the source is pasted at (-x, -y). Premul + Src keeps the source's own alpha
            // where it exists, so an inner crop of an alpha PNG is unchanged; the remaining
            // outpaint area stays opaque grey.
            using var canvas = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var surface = new SKCanvas(canvas))
            {
                surface.Clear(CanvasFill);
                using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
                surface.DrawImage(full, -x, -y, paint);
            }

            using var image = SKImage.FromBitmap(canvas);
            if (image is null)
            {
                return null;
            }

            var output = ResolveCropPath(sessionId, nodeId);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // Skia's native PNG writer: far fewer copies than the shared Magick path and no
            // OpenMP thread pool. File.Create truncates, so a re-crop overwrites in place.
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(output))
            {
                data.SaveTo(stream);
            }

            return File.Exists(output) ? output : null;
        }, ct);
    }

    /// <summary>
    /// Deletes one session's crop directory. Never throws: a missing directory is a no-op
    /// and each file delete is guarded individually (a locked file cannot abort the rest).
    /// </summary>
    public static void CleanupSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        DeleteDirectorySafe(Path.Combine(CropsRootDirectory, sessionId));
    }

    /// <summary>
    /// Deletes every crop session directory (orphans from a previous run). Safe to call at
    /// startup: the app is single-instance, so no live session can exist. Never throws.
    /// </summary>
    public static void CleanupAll()
    {
        try
        {
            if (!Directory.Exists(CropsRootDirectory))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(CropsRootDirectory))
            {
                DeleteDirectorySafe(directory);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[crop] cleanup-all failed: {ex.Message}");
        }
    }

    private static void DeleteDirectorySafe(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[crop] delete failed '{file}': {ex.Message}");
                }
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[crop] cleanup failed '{directory}': {ex.Message}");
        }
    }
}
