using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using ZIV.Imaging.Codecs.Skia;

namespace ZivAiEditor.UI.Imaging;

/// <summary>
/// Crops a source image to a pixel rectangle and writes the result as a new PNG
/// (Step 9C.4). It reuses the shared ZIV.Imaging <see cref="SkiaCodec"/> for decode /
/// encode (Z13 / Z15) and Skia's <c>SKImage.Subset</c> for the crop geometry — ZIV.Imaging
/// exposes no crop primitive. The source file is never modified (Z24 / SPEC §3.9).
///
/// <para>Pure BCL + SkiaSharp + ZIV.Imaging, so it is unit-testable without a UI thread
/// or GPU.</para>
/// </summary>
public static class ImageCropper
{
    private const string CropSuffix = "_crop_";
    private const string OutputFolderName = "output";
    private const string DefaultStem = "zivai";

    /// <summary>
    /// Picks a collision-free output path:
    /// <c>&lt;source dir&gt;/&lt;stem&gt;_crop_&lt;yyyyMMdd_HHmmss&gt;.png</c>, appending
    /// <c>_1</c>, <c>_2</c>… on collision. Falls back to <c>&lt;program dir&gt;/output/</c>
    /// when the source path is blank (mirrors the Python no-source branch, SPEC §3.9).
    /// </summary>
    public static string ResolveOutputPath(string? sourceImagePath, DateTimeOffset now)
    {
        string directory;
        string stem;

        if (!string.IsNullOrWhiteSpace(sourceImagePath))
        {
            var full = Path.GetFullPath(sourceImagePath);
            directory = Path.GetDirectoryName(full) ?? AppContext.BaseDirectory;
            stem = Path.GetFileNameWithoutExtension(full);
        }
        else
        {
            directory = Path.Combine(AppContext.BaseDirectory, OutputFolderName);
            stem = DefaultStem;
        }

        if (string.IsNullOrEmpty(stem))
        {
            stem = DefaultStem;
        }

        Directory.CreateDirectory(directory);

        var stamp = now.ToString("yyyyMMdd_HHmmss");
        var candidate = Path.Combine(directory, $"{stem}{CropSuffix}{stamp}.png");
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{stem}{CropSuffix}{stamp}_{suffix}.png");
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// Crops <paramref name="sourceImagePath"/> to the given pixel rectangle and writes a
    /// new PNG beside it. Returns the output path, or <c>null</c> on any failure (missing
    /// source, decode / encode failure, empty rectangle). The whole operation runs off the
    /// caller's thread (Z11).
    /// </summary>
    public static Task<string?> CropAsync(
        string sourceImagePath,
        int x,
        int y,
        int width,
        int height,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath)
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

            var rect = SKRectI.Intersect(
                new SKRectI(x, y, x + width, y + height),
                new SKRectI(0, 0, full.Width, full.Height));
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return null;
            }

            using var cropped = full.Subset(rect);
            if (cropped is null)
            {
                return null;
            }

            var output = ResolveOutputPath(sourceImagePath, DateTimeOffset.Now);

            // Encode with Skia's native PNG writer: far fewer copies than the shared
            // Magick path (no BGRA -> byte[] -> MagickImage round-trip) and no OpenMP
            // thread pool, so confirming a crop on a large edited image stays light.
            using var data = cropped.Encode(SKEncodedImageFormat.Png, 100);
            using (var stream = File.Create(output))
            {
                data.SaveTo(stream);
            }

            return File.Exists(output) ? output : null;
        }, ct);
    }
}
