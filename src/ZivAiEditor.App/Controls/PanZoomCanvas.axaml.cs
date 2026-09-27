using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Dumb bitmap renderer for the image preview (Z4/N2): self-drawn replacement for the
/// former third-party image box. It holds only the bitmap, the integer zoom (percent) and the
/// pan offset; all zoom / pan state lives in <see cref="ZivAiEditor.UI.Imaging.ImageViewModel"/>
/// and is pushed here by <see cref="ImagePreview"/>, which also routes pointer input via
/// <c>AddHandler</c> on this control (so none of the input semantics live here).
///
/// <para><b>Render rule</b> mirrors <c>ImageViewModel.OriginX/OriginY</c> exactly, and the
/// lock is decided per <b>view</b>, not per axis (B12-follow): when <b>either</b> axis
/// overflows, <b>both</b> axes draw at <c>-Offset</c>; only when <b>both</b> scaled axes fit
/// is the bitmap centered on both.</para>
/// </summary>
public partial class PanZoomCanvas : UserControl
{
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16));

    private Bitmap? _image;

    public PanZoomCanvas()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The bitmap to draw, or <c>null</c>. The control takes ownership of the assigned
    /// bitmap and disposes the previously owned one when it is replaced or cleared (safe
    /// alongside <see cref="ImagePreview"/>'s own disposal: Avalonia's
    /// <c>Bitmap.Dispose</c> is idempotent).
    /// </summary>
    public Bitmap? Image
    {
        get => _image;
        set
        {
            var previous = _image;
            if (ReferenceEquals(previous, value))
            {
                return;
            }

            _image = value;
            previous?.Dispose();
            InvalidateVisual();
        }
    }

    /// <summary>Zoom as an integer percentage (100 = actual size).</summary>
    public int Zoom { get; set; } = 100;

    /// <summary>Pan offset in device pixels; meaningful only on an overflowing axis.</summary>
    public Vector Offset { get; set; }

    /// <summary>The drawable surface size; the same value the view-model uses as its viewport.</summary>
    public Size Viewport => Bounds.Size;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));

        var image = _image;
        if (image is null)
        {
            return;
        }

        var zw = Zoom / 100.0;
        var sw = image.PixelSize.Width * zw;
        var sh = image.PixelSize.Height * zw;
        // B12-follow: lock per view, not per axis — if either axis overflows both draw at
        // -Offset; only a both-fit (locked) view is centered.
        var unlocked = sw > Bounds.Width || sh > Bounds.Height;
        var x = unlocked ? -Offset.X : (Bounds.Width - sw) / 2.0;
        var y = unlocked ? -Offset.Y : (Bounds.Height - sh) / 2.0;
        context.DrawImage(image, new Rect(x, y, sw, sh));
    }
}
