using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Self-drawn swipe-compare overlay (Step 9C.2-C). It renders on top of the
/// <c>AdvancedImageBox</c> (which keeps showing the current image on the right side):
/// the overlay draws the <b>parent</b> (reference) image on the left of a vertical
/// divider and fills everything outside the parent with the canvas background.
///
/// <para><b>Why self-drawn</b> (see DEVLOG Step 9C.2-C): drawing both bitmaps through
/// the shared <see cref="ImageViewModel"/> transform guarantees the two layers stay
/// pixel-aligned through pan / zoom, and gives exact control over the background fill
/// for the "simplified alignment" case — without reparenting a second <c>Image</c>
/// into the renderer's template, and with no new NuGet.</para>
///
/// <para><b>Simplified alignment</b>: when the parent size differs from the current
/// canvas (typical after an outpaint), the parent is drawn <b>centered</b> on the
/// current image rect and the surrounding region is filled with the canvas background
/// (not scaled to fit). Precise outpaint geometry is a registered follow-up — it is
/// not currently carried through <c>EditNode</c> / the IPC <c>result</c> frame, so the
/// offset is unavailable in C# (see DEVLOG / OPTIMIZATION legacy items).</para>
/// </summary>
public partial class CompareOverlay : UserControl
{
    private static readonly IBrush DividerBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x6D, 0xF0));
    private static readonly IPen DividerPen = new Pen(DividerBrush, 2);

    private ImageViewModel? _model;
    private Bitmap? _parent;
    private IBrush _background = Brushes.Transparent;

    public CompareOverlay()
    {
        InitializeComponent();
        IsHitTestVisible = false;
    }

    /// <summary>Canvas background fill used outside the parent image (matches the image area).</summary>
    public IBrush BackgroundFill
    {
        get => _background;
        set
        {
            _background = value ?? Brushes.Transparent;
            InvalidateVisual();
        }
    }

    /// <summary>Shared zoom / pan model; the overlay must use the same instance as the box.</summary>
    public void Attach(ImageViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        InvalidateVisual();
    }

    /// <summary>Sets the parent (reference) bitmap drawn on the left of the divider.</summary>
    public void SetParent(Bitmap? parent)
    {
        _parent = parent;
        InvalidateVisual();
    }

    /// <summary>Divider position as a fraction of the control width, clamped to [0, 1].</summary>
    public double Divider { get; set; } = 0.5;

    /// <summary>True when there is a parent bitmap, an image model, and a drawable size.</summary>
    public bool CanDraw => _parent is not null && _model is { HasImage: true } && Bounds.Width > 0;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var model = _model;
        if (_parent is null || model is null || !model.HasImage || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        var dividerX = Math.Clamp(Divider, 0.0, 1.0) * width;

        // Everything is confined to the left of the divider.
        using var clip = context.PushClip(new Rect(0, 0, dividerX, height));

        // The edit pipeline resizes the source to the target size before inference, so
        // the output canvas and the (resized) source share dimensions. Draw the parent
        // over the full current-image rect so the two align (precise outpaint geometry
        // is a registered follow-up).
        var (currentX, currentY) = model.ImageToViewport(0, 0);
        var currentW = model.ScaledWidth;
        var currentH = model.ScaledHeight;
        context.DrawImage(_parent, new Rect(currentX, currentY, currentW, currentH));

        context.DrawLine(DividerPen, new Point(dividerX, 0), new Point(dividerX, height));
    }
}