using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Self-drawn mask overlay (Step 9C.7): blits a display-only <see cref="WriteableBitmap"/>
/// of the mask buffer where painted pixels become semi-transparent red. Like
/// <see cref="CropOverlay"/> it draws through the shared <see cref="ImageViewModel"/>
/// transform, so the mask stays aligned through pan / zoom. It is
/// <c>IsHitTestVisible = false</c> — <see cref="ImagePreview"/> owns pointer input and just
/// tells this control to repaint. The bitmap is <b>not</b> the drawing engine (D3): it is
/// rebuilt from the buffer only when marked dirty, and disposed on state change / close (Z9).
/// </summary>
public partial class MaskOverlay : UserControl
{
    /// <summary>Semi-transparent red for a painted pixel (BGRA bytes, display only).</summary>
    private const byte Red = 0x80;

    private ImageViewModel? _model;
    private MaskState? _state;
    private WriteableBitmap? _bitmap;
    private bool _dirty = true;

    public MaskOverlay()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        DetachedFromVisualTree += (_, _) => DisposeBitmap();
    }

    /// <summary>Shared zoom / pan model; must be the same instance as the image box.</summary>
    public void Attach(ImageViewModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        InvalidateVisual();
    }

    /// <summary>The mask state to render. Changing the state drops the cached bitmap.</summary>
    public void SetState(MaskState state)
    {
        if (!ReferenceEquals(_state, state))
        {
            _state = state;
            DisposeBitmap();
            _dirty = true;
        }

        InvalidateVisual();
    }

    /// <summary>Marks the cached bitmap stale so the next render rebuilds it.</summary>
    public void MarkDirty()
    {
        _dirty = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var model = _model;
        var state = _state;
        if (model is null || state is null || !state.HasImage || !state.HasContent
            || !model.HasImage || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var (left, top) = model.ImageToViewport(0, 0);
        var (right, bottom) = model.ImageToViewport(state.Width, state.Height);
        var dest = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        if (dest.Width <= 0 || dest.Height <= 0)
        {
            return;
        }

        if (_dirty || _bitmap is null
            || _bitmap.PixelSize.Width != state.Width || _bitmap.PixelSize.Height != state.Height)
        {
            Rebuild(state);
        }

        if (_bitmap is not null)
        {
            context.DrawImage(_bitmap, dest);
        }
    }

    private void Rebuild(MaskState state)
    {
        DisposeBitmap();

        var pixels = state.CopyPixels();
        var width = state.Width;
        var height = state.Height;
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            _dirty = false;
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var buffer = bitmap.Lock())
        {
            var rowBytes = buffer.RowBytes;
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                var source = y * width;
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 4;
                    if (pixels[source + x] != MaskState.Off)
                    {
                        // BGRA, premultiplied: blue = 0, green = 0, red = 0x80, alpha = 0x80.
                        row[offset] = 0x00;
                        row[offset + 1] = 0x00;
                        row[offset + 2] = Red;
                        row[offset + 3] = Red;
                    }
                    else
                    {
                        row[offset] = 0x00;
                        row[offset + 1] = 0x00;
                        row[offset + 2] = 0x00;
                        row[offset + 3] = 0x00;
                    }
                }

                Marshal.Copy(row, 0, IntPtr.Add(buffer.Address, y * rowBytes), row.Length);
            }
        }

        _bitmap = bitmap;
        _dirty = false;
    }

    private void DisposeBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
