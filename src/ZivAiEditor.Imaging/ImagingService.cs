using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Contracts.Imaging;

namespace ZivAiEditor.Imaging;

/// <summary>
/// Imaging-domain facade (module-boundary migration step 4): the <see cref="IImagingService"/>
/// implementation over the local raster primitives (<see cref="ImageCropper"/> /
/// <see cref="MaskExporter"/> / <see cref="MaskFeather"/>). The UI / App depend on the
/// <see cref="IImagingService"/> port only, never on these types.
///
/// <para>Pure BCL + SkiaSharp + ZIV.Imaging; no Avalonia / GPU dependency.</para>
/// </summary>
public sealed class ImagingService : IImagingService
{
    public Task<string?> CropAsync(
        string sourceImagePath,
        string sessionId,
        string nodeId,
        int x,
        int y,
        int width,
        int height,
        CancellationToken ct = default)
        => ImageCropper.CropAsync(sessionId, nodeId, sourceImagePath, x, y, width, height, ct);

    public Task<string?> ExportMaskAsync(
        string sessionId,
        string nodeId,
        byte[] pixels,
        int width,
        int height,
        int featherPx = 0,
        CancellationToken ct = default)
        => MaskExporter.ExportAsync(sessionId, nodeId, pixels, width, height, featherPx, ct);

    public Task<(int Width, int Height, byte[] Pixels)?> LoadMaskAsync(string? path, CancellationToken ct = default)
        => MaskExporter.TryLoadAsync(path, ct);

    public string ResolveMaskPath(string sessionId, string nodeId)
        => MaskExporter.ResolveMaskPath(sessionId, nodeId);

    public byte[] FeatherMask(byte[] pixels, int width, int height, int radiusPx)
        => MaskFeather.Apply(pixels, width, height, radiusPx);

    public void CleanupSession(string? sessionId)
    {
        ImageCropper.CleanupSession(sessionId);
        MaskExporter.CleanupSession(sessionId);
    }

    public void CleanupAll()
    {
        ImageCropper.CleanupAll();
        MaskExporter.CleanupAll();
    }

    public void CleanupNode(string? sessionId, IReadOnlyList<string> nodeIds)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || nodeIds is null || nodeIds.Count == 0)
        {
            return;
        }

        foreach (var nodeId in nodeIds)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                continue;
            }

            DeleteFileSafe(ImageCropper.ResolveCropPath(sessionId, nodeId));
            DeleteFileSafe(MaskExporter.ResolveMaskPath(sessionId, nodeId));
        }
    }

    private static void DeleteFileSafe(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[imaging] node cleanup failed '{path}': {ex.Message}");
        }
    }
}
