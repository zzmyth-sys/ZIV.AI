using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Self-drawn crop overlay (Step 9C.4): darkens the area outside the selection and draws
/// the rectangle border plus its 8 handles (4 corners + 4 edge midpoints). Like
/// <see cref="CompareOverlay"/> it draws through the shared <see cref="ImageViewModel"/>
/// transform, so the frame stays aligned through pan / zoom. It is
/// <c>IsHitTestVisible = false</c> — <see cref="ImagePreview"/> owns pointer input and
/// just tells this control to repaint.
/// </summary>
public partial class CropOverlay : UserControl
{
    private static readonly IBrush MaskBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
    private static readonly IPen BorderPen = new Pen(Brushes.White, 2);
    private static readonly IBrush HandleFill = Brushes.White;
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x6D, 0xF0)), 1);

    private const double HandleSize = 10.0;

    private ImageViewModel? _model;
    private CropState? _state;

    public CropOverlay()
    {
        InitializeComponent();
        IsHitTestVisible = false;
    }

    /// <summary>Shared zoom / pan model; must be the same instance as the image box.</summary>
    public void Attach(ImageViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        InvalidateVisual();
    }

    /// <summary>The crop selection state to render.</summary>
    public void SetState(CropState state)
    {
        _state = state;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var model = _model;
        var state = _state;
        if (model is null || state is null || !state.IsActive || !state.HasRect
            || !model.HasImage || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var (left, top) = model.ImageToViewport(state.X, state.Y);
        var (right, bottom) = model.ImageToViewport(state.X + state.Width, state.Y + state.Height);
        var rect = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var full = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var frame = rect.Intersect(full);
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        // Darken everything outside the frame with four surrounding bands.
        context.FillRectangle(MaskBrush, new Rect(0, 0, full.Width, frame.Top));
        context.FillRectangle(MaskBrush, new Rect(0, frame.Bottom, full.Width, full.Height - frame.Bottom));
        context.FillRectangle(MaskBrush, new Rect(0, frame.Top, frame.Left, frame.Height));
        context.FillRectangle(MaskBrush, new Rect(frame.Right, frame.Top, full.Width - frame.Right, frame.Height));

        // Border + 8 handles.
        context.DrawRectangle(null, BorderPen, rect);

        var midX = (rect.Left + rect.Right) / 2.0;
        var midY = (rect.Top + rect.Bottom) / 2.0;
        DrawHandle(context, rect.Left, rect.Top);
        DrawHandle(context, midX, rect.Top);
        DrawHandle(context, rect.Right, rect.Top);
        DrawHandle(context, rect.Right, midY);
        DrawHandle(context, rect.Right, rect.Bottom);
        DrawHandle(context, midX, rect.Bottom);
        DrawHandle(context, rect.Left, rect.Bottom);
        DrawHandle(context, rect.Left, midY);
    }

    private static void DrawHandle(DrawingContext context, double centerX, double centerY)
    {
        var handle = new Rect(
            centerX - HandleSize / 2.0,
            centerY - HandleSize / 2.0,
            HandleSize,
            HandleSize);
        context.FillRectangle(HandleFill, handle);
        context.DrawRectangle(null, HandlePen, handle);
    }
}
