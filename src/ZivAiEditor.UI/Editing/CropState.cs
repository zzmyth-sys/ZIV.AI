namespace ZivAiEditor.UI.Editing;

/// <summary>Which part of the crop rectangle a pointer is over (Step 9C.6-B).</summary>
public enum CropHandle
{
    None,
    Move,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// Pure crop-selection state for the preview window (Step 9C.6-B). The rectangle is
/// stored in <b>source-image pixel</b> coordinates (SPEC §3.9); the App overlay maps it
/// to and from the viewport through <c>ImageViewModel</c>. It carries <b>no</b> Avalonia
/// dependency, so the geometry / hit-testing / state machine are unit-testable (Z3/Z6).
///
/// <para><b>Interaction</b> (user ruling): the rectangle can be moved as a whole and
/// resized by its 8 handles (4 corners + 4 edge midpoints); pressing outside it starts a
/// new rectangle. The initial rectangle is the node's previous crop when one exists,
/// otherwise a 75% centered box (<see cref="SetDefaultRect"/>).</para>
///
/// Lifecycle: <see cref="Enter"/> → (<see cref="SetRect"/> / <see cref="SetDefaultRect"/>
/// / drag) → <see cref="BeginDrag"/> / <see cref="UpdateDrag"/> / <see cref="EndDrag"/> →
/// <see cref="Exit"/>. The rectangle is always clamped inside the image and never smaller
/// than <see cref="MinSize"/> on either edge.
/// </summary>
public sealed class CropState
{
    /// <summary>Smallest allowed selection edge, in image pixels.</summary>
    public const double MinSize = 16.0;

    /// <summary>Fraction of the image used by the default (centered) rectangle.</summary>
    public const double DefaultFraction = 0.75;

    private double _imageWidth;
    private double _imageHeight;

    private double _x;
    private double _y;
    private double _width;
    private double _height;

    private double _startX;
    private double _startY;
    private double _origX;
    private double _origY;
    private double _origWidth;
    private double _origHeight;

    /// <summary>True while a brand-new rectangle is being dragged out (not move / resize).</summary>
    private bool _building;

    /// <summary>True while the crop tool is active in the preview.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True when a (valid) selection rectangle exists.</summary>
    public bool HasRect { get; private set; }

    /// <summary>True while a build / move / resize drag is in progress.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>The handle currently being dragged (or <see cref="CropHandle.None"/>).</summary>
    public CropHandle DragHandle { get; private set; } = CropHandle.None;

    public double X => _x;

    public double Y => _y;

    public double Width => _width;

    public double Height => _height;

    /// <summary>True when a source image with a non-zero size is known.</summary>
    public bool HasImage => _imageWidth > 0 && _imageHeight > 0;

    /// <summary>Sets the source-image pixel bounds the rectangle is clamped to.</summary>
    public void SetImageBounds(double width, double height)
    {
        _imageWidth = width > 0 ? width : 0;
        _imageHeight = height > 0 ? height : 0;
        if (!HasImage)
        {
            HasRect = false;
        }
    }

    /// <summary>Enters crop mode with no rectangle yet.</summary>
    public void Enter()
    {
        IsActive = true;
        IsDragging = false;
        DragHandle = CropHandle.None;
        _building = false;
        HasRect = false;
        _x = _y = _width = _height = 0;
    }

    /// <summary>Leaves crop mode and drops the rectangle.</summary>
    public void Exit()
    {
        IsActive = false;
        HasRect = false;
        IsDragging = false;
        DragHandle = CropHandle.None;
        _building = false;
        _x = _y = _width = _height = 0;
    }

    /// <summary>
    /// Sets the rectangle explicitly (used to restore a node's previous crop). The
    /// rectangle is clamped to the image; <see cref="HasRect"/> ends up <c>false</c> when
    /// the clamped size is below <see cref="MinSize"/> (e.g. the image changed size).
    /// </summary>
    public void SetRect(double x, double y, double width, double height)
    {
        if (!HasImage)
        {
            HasRect = false;
            return;
        }

        var left = Math.Clamp(x, 0, _imageWidth);
        var top = Math.Clamp(y, 0, _imageHeight);
        var right = Math.Clamp(x + width, 0, _imageWidth);
        var bottom = Math.Clamp(y + height, 0, _imageHeight);

        _x = left;
        _y = top;
        _width = Math.Max(0, right - left);
        _height = Math.Max(0, bottom - top);
        HasRect = _width >= MinSize && _height >= MinSize;
    }

    /// <summary>Selects a centered rectangle covering <see cref="DefaultFraction"/> of the image.</summary>
    public void SetDefaultRect()
    {
        if (!HasImage)
        {
            return;
        }

        _width = _imageWidth * DefaultFraction;
        _height = _imageHeight * DefaultFraction;
        _x = (_imageWidth - _width) / 2.0;
        _y = (_imageHeight - _height) / 2.0;
        HasRect = _width >= MinSize && _height >= MinSize;
    }

    /// <summary>
    /// Returns which handle (or <see cref="CropHandle.Move"/> inside the rectangle) lies
    /// under the given image-space point, within <paramref name="tolerance"/> image pixels.
    /// </summary>
    public CropHandle HitTest(double imageX, double imageY, double tolerance)
    {
        if (!HasRect)
        {
            return CropHandle.None;
        }

        var t = tolerance > 0 ? tolerance : 0;
        var left = _x;
        var top = _y;
        var right = _x + _width;
        var bottom = _y + _height;
        var midX = (left + right) / 2.0;
        var midY = (top + bottom) / 2.0;

        bool Near(double a, double b) => Math.Abs(a - b) <= t;

        // Corners win over edge midpoints, which win over the interior.
        if (Near(imageX, left) && Near(imageY, top))
        {
            return CropHandle.TopLeft;
        }

        if (Near(imageX, right) && Near(imageY, top))
        {
            return CropHandle.TopRight;
        }

        if (Near(imageX, right) && Near(imageY, bottom))
        {
            return CropHandle.BottomRight;
        }

        if (Near(imageX, left) && Near(imageY, bottom))
        {
            return CropHandle.BottomLeft;
        }

        if (Near(imageX, midX) && Near(imageY, top))
        {
            return CropHandle.Top;
        }

        if (Near(imageX, right) && Near(imageY, midY))
        {
            return CropHandle.Right;
        }

        if (Near(imageX, midX) && Near(imageY, bottom))
        {
            return CropHandle.Bottom;
        }

        if (Near(imageX, left) && Near(imageY, midY))
        {
            return CropHandle.Left;
        }

        var insideX = imageX >= left && imageX <= right;
        var insideY = imageY >= top && imageY <= bottom;
        return insideX && insideY ? CropHandle.Move : CropHandle.None;
    }

    /// <summary>
    /// Starts a drag at an image-space point: outside the rectangle (or with no
    /// rectangle) it begins building a new one, otherwise it moves / resizes.
    /// </summary>
    public bool BeginDrag(double imageX, double imageY, double tolerance)
    {
        if (!IsActive || !HasImage)
        {
            return false;
        }

        var hit = HasRect ? HitTest(imageX, imageY, tolerance) : CropHandle.None;

        _startX = imageX;
        _startY = imageY;
        _origX = _x;
        _origY = _y;
        _origWidth = _width;
        _origHeight = _height;

        if (hit == CropHandle.None)
        {
            _building = true;
            DragHandle = CropHandle.None;
            _x = Math.Clamp(imageX, 0, _imageWidth);
            _y = Math.Clamp(imageY, 0, _imageHeight);
            _width = 0;
            _height = 0;
            HasRect = true;
        }
        else
        {
            _building = false;
            DragHandle = hit;
        }

        IsDragging = true;
        return true;
    }

    /// <summary>Updates the active drag to the given image-space point.</summary>
    public void UpdateDrag(double imageX, double imageY)
    {
        if (!IsDragging || !HasImage)
        {
            return;
        }

        var cx = Math.Clamp(imageX, 0, _imageWidth);
        var cy = Math.Clamp(imageY, 0, _imageHeight);

        if (_building)
        {
            var sx = Math.Clamp(_startX, 0, _imageWidth);
            var sy = Math.Clamp(_startY, 0, _imageHeight);
            var left = Math.Min(sx, cx);
            var top = Math.Min(sy, cy);
            var right = Math.Max(sx, cx);
            var bottom = Math.Max(sy, cy);
            _x = left;
            _y = top;
            _width = right - left;
            _height = bottom - top;
            return;
        }

        if (DragHandle == CropHandle.Move)
        {
            // Use the raw pointer delta so the rectangle tracks the cursor even when the
            // pointer leaves the image; the result is clamped.
            _x = Math.Clamp(_origX + (imageX - _startX), 0, Math.Max(0, _imageWidth - _origWidth));
            _y = Math.Clamp(_origY + (imageY - _startY), 0, Math.Max(0, _imageHeight - _origHeight));
            _width = _origWidth;
            _height = _origHeight;
            return;
        }

        var leftE = _origX;
        var topE = _origY;
        var rightE = _origX + _origWidth;
        var bottomE = _origY + _origHeight;

        if (DragHandle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft)
        {
            leftE = Math.Clamp(cx, 0, rightE - MinSize);
        }

        if (DragHandle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight)
        {
            rightE = Math.Clamp(cx, leftE + MinSize, _imageWidth);
        }

        if (DragHandle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight)
        {
            topE = Math.Clamp(cy, 0, bottomE - MinSize);
        }

        if (DragHandle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight)
        {
            bottomE = Math.Clamp(cy, topE + MinSize, _imageHeight);
        }

        _x = leftE;
        _y = topE;
        _width = Math.Max(MinSize, rightE - leftE);
        _height = Math.Max(MinSize, bottomE - topE);
    }

    /// <summary>
    /// Ends the active drag. A freshly built rectangle below <see cref="MinSize"/> on
    /// either edge is discarded; otherwise the rectangle is normalized / clamped.
    /// </summary>
    public void EndDrag()
    {
        if (!IsDragging)
        {
            return;
        }

        IsDragging = false;
        DragHandle = CropHandle.None;

        if (_building)
        {
            _building = false;
            if (_width < MinSize || _height < MinSize)
            {
                HasRect = false;
                _x = _y = _width = _height = 0;
                return;
            }
        }

        Normalize();
    }

    /// <summary>
    /// Projects the rectangle onto integer pixel coordinates inside the image. Returns
    /// <c>false</c> when there is no usable (non-empty) rectangle.
    /// </summary>
    public bool TryGetPixelRect(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (!HasRect || !HasImage)
        {
            return false;
        }

        var left = (int)Math.Round(Math.Clamp(_x, 0, _imageWidth));
        var top = (int)Math.Round(Math.Clamp(_y, 0, _imageHeight));
        var right = (int)Math.Round(Math.Clamp(_x + _width, 0, _imageWidth));
        var bottom = (int)Math.Round(Math.Clamp(_y + _height, 0, _imageHeight));
        width = right - left;
        height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        x = left;
        y = top;
        return true;
    }

    private void Normalize()
    {
        _width = Math.Clamp(_width, MinSize, _imageWidth);
        _height = Math.Clamp(_height, MinSize, _imageHeight);
        _x = Math.Clamp(_x, 0, Math.Max(0, _imageWidth - _width));
        _y = Math.Clamp(_y, 0, Math.Max(0, _imageHeight - _height));
    }
}
