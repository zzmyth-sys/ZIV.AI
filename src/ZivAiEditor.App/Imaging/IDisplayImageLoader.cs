using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;

namespace ZivAiEditor.App.Imaging;

/// <summary>
/// A decoded <b>display</b> image plus the <b>original</b> pixel size it was derived from. The
/// bitmap is a proxy (≤ <see cref="IDisplayImageLoader.MaxDisplaySide"/> long side);
/// <see cref="DisplayPixelSize"/> is that bitmap's real size and is the <b>single coordinate
/// space</b> the preview view-model / renderer / overlays all work in (they never need the
/// original size). <see cref="OriginalPixelSize"/> is carried only for two non-display uses:
/// the size badge and the crop scale (display → original) so crop output keeps original precision.
/// </summary>
/// <param name="Bitmap">The decoded bitmap to draw (proxy or the source when already small).</param>
/// <param name="DisplayPixelSize">The drawn bitmap's pixel size — the UI's coordinate space.</param>
/// <param name="OriginalPixelSize">The full-size source pixel size (device pixels, no DPI scaling).</param>
/// <param name="ProxyPath">The proxy file used, or <c>null</c> when the source was drawn directly.</param>
public sealed record DisplayImage(
    Bitmap Bitmap,
    PixelSize DisplayPixelSize,
    PixelSize OriginalPixelSize,
    string? ProxyPath);

/// <summary>
/// App-layer port for producing a bounded-size <b>display</b> bitmap from an arbitrary image file
/// (Q9: the port lives in the view layer; <c>ZivAiEditor.UI</c> stays Avalonia-free). One place
/// decides how an image is decoded for display, so preview / chat / attachment thumbnails all
/// share the same 8K-safe downsampling and proxy cache. The rendering/pipeline path is untouched:
/// crop and mask edits still read the full-resolution source.
/// </summary>
public interface IDisplayImageLoader
{
    /// <summary>Long-side cap for a display bitmap (2.5K). Mirrors the proxy threshold.</summary>
    const int MaxDisplaySide = 2560;

    /// <summary>
    /// Decodes <paramref name="path"/> for display, capping the long side at
    /// <paramref name="maxSide"/>. An image already within the cap is returned at full size; a
    /// larger one goes through the on-disk proxy cache. Throws when the file cannot be decoded.
    /// </summary>
    DisplayImage LoadDisplay(string path, int maxSide = MaxDisplaySide);

    /// <summary>Off-thread (Z11) variant of <see cref="LoadDisplay"/>; <c>null</c> on failure.</summary>
    Task<DisplayImage?> LoadDisplayAsync(string path, int maxSide = MaxDisplaySide, CancellationToken ct = default);
}
