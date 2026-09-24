using System.Threading;
using System.Threading.Tasks;

namespace ZivAiEditor.Contracts.Imaging;

/// <summary>
/// Kernel port for the imaging domain (module-boundary migration step 4): local raster
/// operations — crop / outpaint, mask encode / decode and feathering — plus their temp-area
/// cleanup. Implemented by <c>ZivAiEditor.Imaging.ImagingService</c>; the UI / App depend only
/// on this interface (Q9: ports live in the kernel).
///
/// <para>The hand-drawn mask buffer itself is pure <c>byte[]</c> (0 / 255) and stays in the UI
/// domain; only the file / raster boundary crosses this port.</para>
/// </summary>
public interface IImagingService
{
    /// <summary>
    /// Crops / outpaints <paramref name="sourceImagePath"/> to the pixel rectangle and writes
    /// (overwriting) the per-node crop temp file. X / Y may be negative (outpaint). Returns the
    /// output path, or <c>null</c> on any failure. Runs off the caller's thread (Z11).
    /// </summary>
    Task<string?> CropAsync(
        string sourceImagePath,
        string sessionId,
        string nodeId,
        int x,
        int y,
        int width,
        int height,
        CancellationToken ct = default);

    /// <summary>
    /// Encodes <paramref name="pixels"/> (row-major, <paramref name="width"/>×<paramref name="height"/>)
    /// as an 8-bit grayscale PNG, applying <paramref name="featherPx"/> first when positive.
    /// Returns the output path, or <c>null</c> on any failure. Runs off the caller's thread (Z11).
    /// </summary>
    Task<string?> ExportMaskAsync(
        string sessionId,
        string nodeId,
        byte[] pixels,
        int width,
        int height,
        int featherPx = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Decodes a mask PNG and thresholds it back to the hard 0 / 255 contour. Returns the buffer
    /// with its dimensions, or <c>null</c> on any failure. Runs off the caller's thread (Z11).
    /// </summary>
    Task<(int Width, int Height, byte[] Pixels)?> LoadMaskAsync(string? path, CancellationToken ct = default);

    /// <summary>The mask temp path for one node (used to build a <c>MaskSpec</c> path).</summary>
    string ResolveMaskPath(string sessionId, string nodeId);

    /// <summary>
    /// Blooms a hard 0 / 255 mask buffer into a soft grayscale ramp (<paramref name="radiusPx"/>
    /// clamped; 0 returns a clone). The same pure function the export path uses, exposed so the
    /// overlay display matches the exported mask.
    /// </summary>
    byte[] FeatherMask(byte[] pixels, int width, int height, int radiusPx);

    /// <summary>Deletes one session's crop + mask temp directories. Never throws.</summary>
    void CleanupSession(string? sessionId);

    /// <summary>Deletes every crop + mask session directory (startup orphan cleanup). Never throws.</summary>
    void CleanupAll();
}
