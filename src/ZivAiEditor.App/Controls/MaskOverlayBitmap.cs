using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Builds / patches the display-only semi-transparent red overlay bitmap for a mask (Step
/// 9C.7 / E1). Shared by the preview's <see cref="MaskOverlay"/> and the chat bubble, so both
/// surfaces render the mask byte-identically. The input is the (optionally feathered)
/// grayscale buffer; the live 0 / 255 buffer is just the radius-0 case. This is <b>not</b> the
/// drawing engine (D3): it is display only and never persisted.
/// </summary>
internal static class MaskOverlayBitmap
{
    /// <summary>Semi-transparent red for a painted pixel (BGRA bytes, display only).</summary>
    private const byte Red = 0x80;

    /// <summary>
    /// Returns a premultiplied BGRA bitmap where each grayscale <paramref name="display"/>
    /// value becomes red scaled by that value (0 = transparent). Returns <c>null</c> for a
    /// bad size / short buffer.
    /// </summary>
    public static WriteableBitmap? Build(byte[] display, int width, int height)
    {
        if (width <= 0 || height <= 0 || display.Length < width * height)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        WriteRegion(bitmap, display, 0, 0, width, height);
        return bitmap;
    }

    /// <summary>
    /// Writes the <paramref name="width"/>×<paramref name="height"/> rectangle of
    /// <paramref name="values"/> (row-major, region-local) into <paramref name="bitmap"/> at
    /// (<paramref name="x"/>,<paramref name="y"/>). Used both for the full build and for the
    /// incremental in-stroke patch (R1).
    /// </summary>
    public static void WriteRegion(WriteableBitmap bitmap, byte[] values, int x, int y, int width, int height)
    {
        if (bitmap is null || width <= 0 || height <= 0 || values.Length < width * height)
        {
            return;
        }

        using var buffer = bitmap.Lock();
        var rowBytes = buffer.RowBytes;
        var row = new byte[width * 4];
        for (var ry = 0; ry < height; ry++)
        {
            var source = ry * width;
            for (var rx = 0; rx < width; rx++)
            {
                var offset = rx * 4;
                var value = values[source + rx];
                if (value != 0)
                {
                    // BGRA, premultiplied: red scaled by the (possibly feathered) alpha.
                    row[offset] = 0x00;
                    row[offset + 1] = 0x00;
                    row[offset + 2] = (byte)(value * Red / 255);
                    row[offset + 3] = value;
                }
                else
                {
                    row[offset] = 0x00;
                    row[offset + 1] = 0x00;
                    row[offset + 2] = 0x00;
                    row[offset + 3] = 0x00;
                }
            }

            Marshal.Copy(row, 0, IntPtr.Add(buffer.Address, (y + ry) * rowBytes + x * 4), row.Length);
        }
    }
}
