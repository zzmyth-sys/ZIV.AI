using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace ZivAiEditor.Imaging;

/// <summary>
/// Encodes / decodes the hand-drawn mask as an 8-bit grayscale PNG (Step 9C.7). The mask
/// buffer itself is pure <c>byte[]</c> (0 or 255, D3); Skia is used only here for file IO.
/// With <c>featherPx = 0</c> the exported PNG therefore contains only 0 / 255 per channel
/// (R2); with a positive feather (revised Z19) <see cref="MaskFeather"/> blooms the buffer
/// into a soft grayscale ramp <b>in the same pure function</b> the overlay uses. The backend
/// reads it with <c>Image.open(path).convert("L")</c>; user masks are read as grayscale
/// (<c>binary=False</c>) while the loader thresholds back to the hard contour on reload.
///
/// <para><b>Temporary area</b> (Z14): masks are intermediate products, so they are written
/// under the program directory at <c>_cache/masks/{sessionId}/{nodeId}.png</c> — one file
/// per node, overwritten on redraw (never accumulated), removed when the session closes /
/// resets. Mirrors <see cref="ImageCropper"/>. Pure BCL + SkiaSharp, so it is unit-testable
/// without a UI thread or GPU. Reached through <see cref="IImagingService"/> (module-boundary
/// migration step 4); <c>ZivAiEditor.UI</c> / <c>ZivAiEditor.App</c> do not reference this
/// type directly.</para>
/// </summary>
public static class MaskExporter
{
    private const string CacheFolderName = "_cache";
    private const string MasksFolderName = "masks";
    private const string PngExtension = ".png";

    /// <summary>Decode threshold: a pixel at or above this becomes <c>255</c>, else <c>0</c>.</summary>
    private const byte Threshold = 128;

    /// <summary>The program-directory root of all mask temp files (Z14).</summary>
    public static string MasksRootDirectory
        => Path.Combine(AppContext.BaseDirectory, CacheFolderName, MasksFolderName);

    /// <summary>
    /// The mask temp path for one node: <c>_cache/masks/{sessionId}/{nodeId}.png</c>. One
    /// file per node — redrawing the same node overwrites it.
    /// </summary>
    public static string ResolveMaskPath(string sessionId, string nodeId)
        => Path.Combine(MasksRootDirectory, sessionId, nodeId + PngExtension);

    /// <summary>
    /// Encodes <paramref name="pixels"/> (row-major, <paramref name="width"/>×<paramref name="height"/>)
    /// as an 8-bit grayscale PNG and writes (overwriting)
    /// <c>_cache/masks/{sessionId}/{nodeId}.png</c>. When <paramref name="featherPx"/> is
    /// positive the buffer is first run through <see cref="MaskFeather.Apply"/> (off-thread,
    /// same function as the display), else it stays hard 0 / 255. Returns the output path,
    /// or <c>null</c> on any failure (blank ids, bad dimensions, short buffer, encode
    /// failure). The whole operation runs off the caller's thread (Z11).
    /// </summary>
    public static Task<string?> ExportAsync(
        string sessionId,
        string nodeId,
        byte[] pixels,
        int width,
        int height,
        int featherPx = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(nodeId)
            || pixels is null || width <= 0 || height <= 0
            || pixels.Length < width * height)
        {
            return Task.FromResult<string?>(null);
        }

        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            // Feather off-thread (Z11) with the shared pure function; radius 0 is a clone.
            var encoded = MaskFeather.Apply(pixels, width, height, featherPx);

            var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
            using var bitmap = new SKBitmap(info);
            var span = bitmap.GetPixelSpan();
            var rowBytes = bitmap.RowBytes;
            for (var y = 0; y < height; y++)
            {
                // Respect RowBytes: the row may be padded beyond width bytes.
                encoded.AsSpan(y * width, width).CopyTo(span.Slice(y * rowBytes, width));
            }

            using var image = SKImage.FromBitmap(bitmap);
            if (image is null)
            {
                return null;
            }

            var output = ResolveMaskPath(sessionId, nodeId);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            // File.Create truncates, so a redraw overwrites in place.
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(output))
            {
                data.SaveTo(stream);
            }

            return File.Exists(output) ? output : null;
        }, ct);
    }

    /// <summary>
    /// Decodes a mask PNG and thresholds each pixel (<c>&gt;= 128 → 255</c>, else <c>0</c>).
    /// A feathered PNG is therefore recovered as its hard 50%-contour (≈ the original
    /// boundary); the persisted <c>FeatherPx</c> is re-applied from the <c>MaskSpec</c>.
    /// Returns the buffer with its dimensions, or <c>null</c> on any failure (missing file,
    /// decode failure). Runs off the caller's thread (Z11).
    /// </summary>
    public static Task<(int Width, int Height, byte[] Pixels)?> TryLoadAsync(
        string? path,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Task.FromResult<(int Width, int Height, byte[] Pixels)?>(null);
        }

        ct.ThrowIfCancellationRequested();

        return Task.Run<(int Width, int Height, byte[] Pixels)?>(() =>
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return null;
            }

            var width = bitmap.Width;
            var height = bitmap.Height;
            var result = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    // GetPixel normalizes any decoded color type; the gray channel is used.
                    var gray = bitmap.GetPixel(x, y).Red;
                    result[row + x] = gray >= Threshold ? (byte)255 : (byte)0;
                }
            }

            return (width, height, result);
        }, ct);
    }

    /// <summary>
    /// Deletes one session's mask directory. Never throws: a missing directory is a no-op
    /// and each file delete is guarded individually (a locked file cannot abort the rest).
    /// </summary>
    public static void CleanupSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        DeleteDirectorySafe(Path.Combine(MasksRootDirectory, sessionId));
    }

    /// <summary>
    /// Deletes every mask session directory (orphans from a previous run). Safe to call at
    /// startup: the app is single-instance, so no live session can exist. Never throws.
    /// </summary>
    public static void CleanupAll()
    {
        try
        {
            if (!Directory.Exists(MasksRootDirectory))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(MasksRootDirectory))
            {
                DeleteDirectorySafe(directory);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[mask] cleanup-all failed: {ex.Message}");
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
                    Debug.WriteLine($"[mask] delete failed '{file}': {ex.Message}");
                }
            }

            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[mask] cleanup failed '{directory}': {ex.Message}");
        }
    }
}
