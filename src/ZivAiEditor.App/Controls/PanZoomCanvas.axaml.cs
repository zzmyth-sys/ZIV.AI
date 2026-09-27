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
/// <para><b>Render rule</b> mirrors <c>ImageViewModel.OriginX/OriginY</c> exactly: the image
/// rests <b>centered</b> (<c>(Viewport - Scaled) / 2</c>) at <c>Offset = 0</c> and is drawn at
/// the centered rest position minus the pan offset on <b>each</b> axis. There is no view-level
/// lock — a fitting / smaller-than-viewport image pans just like an overflowing one.</para>
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

    /// <summary>Pan displacement in device pixels from the centered rest position.</summary>
    public Vector Offset { get; set; }

    /// <summary>
    /// The <b>source</b> image size in device pixels (the original, not the proxy's own size). The
    /// bitmap is drawn into a rect of <c>SourceSize × Zoom</c>, so a downscaled proxy stretches to
    /// the original coordinates and the DPI unit matches the view-model (which also works in
    /// original device pixels). Defaults to <c>(0,0)</c>, in which case the bitmap's own pixel
    /// size is used.
    /// </summary>
    public Size SourceSize { get; set; }

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

        var source = SourceSize.Width > 0 && SourceSize.Height > 0
            ? SourceSize
            : new Size(image.PixelSize.Width, image.PixelSize.Height);

        var dest = ComputeDrawRect(source, Zoom, Bounds.Size, Offset);
        context.DrawImage(image, dest);
    }

    /// <summary>
    /// Pure geometry for one draw: the destination rect the <b>source-sized</b> image occupies at
    /// the given integer <paramref name="zoomPercent"/>. Mirrors <c>ImageViewModel.OriginX/OriginY</c>
    /// exactly (free pan, ZIV parity): each axis draws at its centered rest position
    /// <c>(viewport - scaled) / 2</c> minus the pan offset. Exposed for unit testing the renderer
    /// math without a GPU.
    /// </summary>
    public static Rect ComputeDrawRect(Size sourceSize, int zoomPercent, Size viewport, Vector offset)
    {
        var zw = zoomPercent / 100.0;
        var sw = sourceSize.Width * zw;
        var sh = sourceSize.Height * zw;
        var x = (viewport.Width - sw) / 2.0 - offset.X;
        var y = (viewport.Height - sh) / 2.0 - offset.Y;
        return new Rect(x, y, sw, sh);
    }
}
