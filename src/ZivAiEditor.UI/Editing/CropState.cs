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
/// <para><b>Outpaint</b> (Step 9C.4-B): the rectangle may extend <b>outside</b> the image,
/// producing a larger output canvas whose remaining area is filled (see <c>ImageCropper</c>).
/// The rectangle is therefore not clamped inside the image; it is limited only to
/// <see cref="MaxExpandFactor"/>× the image edge, <see cref="MaxPixelCount"/> total pixels,
/// and a positive-area overlap with the source.</para>
///
/// Lifecycle: <see cref="Enter"/> → (<see cref="SetRect"/> / <see cref="SetDefaultRect"/>
/// / drag) → <see cref="BeginDrag"/> / <see cref="UpdateDrag"/> / <see cref="EndDrag"/> →
/// <see cref="Exit"/>. The rectangle is never smaller than <see cref="MinSize"/> on either
/// edge.
/// </summary>
public sealed class CropState
{
    /// <summary>Smallest allowed selection edge, in image pixels.</summary>
    public const double MinSize = 16.0;

    /// <summary>Fraction of the image used by the default (centered) rectangle.</summary>
    public const double DefaultFraction = 0.75;

    /// <summary>Largest output edge, as a multiple of the corresponding image edge (D2).</summary>
    public const double MaxExpandFactor = 2.0;

    /// <summary>Largest output area, in pixels (D2) — bounds an outpaint canvas allocation.</summary>
    public const double MaxPixelCount = 16_000_000.0;

    /// <summary>Absorbs floating-point noise when testing whether a rect leaves the image.</summary>
    private const double BoundaryEpsilon = 1e-6;

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
    /// rectangle is limited by <see cref="ClampToLimits"/> (outer bounds, area, overlap);
    /// <see cref="HasRect"/> ends up <c>false</c> when the size is below
    /// <see cref="MinSize"/> (e.g. the image changed size). X / Y may be negative (outpaint).
    /// </summary>
    public void SetRect(double x, double y, double width, double height)
    {
        if (!HasImage)
        {
            HasRect = false;
            return;
        }

        var cx = x;
        var cy = y;
        var cw = Math.Max(0, width);
        var ch = Math.Max(0, height);
        ClampToLimits(ref cx, ref cy, ref cw, ref ch);

        _x = cx;
        _y = cy;
        _width = cw;
        _height = ch;
        HasRect = _width >= MinSize && _height >= MinSize;
    }

    /// <summary>
    /// Selects a centered rectangle covering <see cref="DefaultFraction"/> of the image. It
    /// is an inner crop, so it is never scaled by the outpaint pixel cap (9C.4-B-P2).
    /// </summary>
    public void SetDefaultRect()
    {
        if (!HasImage)
        {
            return;
        }

        // The default box is always an inner crop, so it keeps the source resolution even
        // when the source itself exceeds the outpaint pixel cap (9C.4-B-P2).
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
            // Build from the raw pointer position (may be outside the image) so a drag that
            // starts in the gray margin and crosses the image produces a valid outpaint rect.
            // The raw rect is finalized by EndDrag → Normalize.
            _building = true;
            DragHandle = CropHandle.None;
            _x = imageX;
            _y = imageY;
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

        if (_building)
        {
            // Raw build: the rect may be outside the image; EndDrag → Normalize limits it.
            var left = Math.Min(_startX, imageX);
            var top = Math.Min(_startY, imageY);
            var right = Math.Max(_startX, imageX);
            var bottom = Math.Max(_startY, imageY);
            _x = left;
            _y = top;
            _width = right - left;
            _height = bottom - top;
            return;
        }

        if (DragHandle == CropHandle.Move)
        {
            // Use the raw pointer delta so the rectangle tracks the cursor even when the
            // pointer leaves the image; the result is limited by ClampToLimits.
            _x = _origX + (imageX - _startX);
            _y = _origY + (imageY - _startY);
            _width = _origWidth;
            _height = _origHeight;
            ClampToLimits(ref _x, ref _y, ref _width, ref _height);
            return;
        }

        var leftE = _origX;
        var topE = _origY;
        var rightE = _origX + _origWidth;
        var bottomE = _origY + _origHeight;

        // Only the min-size relationship is enforced here; the outer / area / overlap
        // limits are applied once by ClampToLimits below (edges may drag outside the image).
        if (DragHandle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft)
        {
            leftE = Math.Min(imageX, rightE - MinSize);
        }

        if (DragHandle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight)
        {
            rightE = Math.Max(imageX, leftE + MinSize);
        }

        if (DragHandle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight)
        {
            topE = Math.Min(imageY, bottomE - MinSize);
        }

        if (DragHandle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight)
        {
            bottomE = Math.Max(imageY, topE + MinSize);
        }

        _x = leftE;
        _y = topE;
        _width = Math.Max(MinSize, rightE - leftE);
        _height = Math.Max(MinSize, bottomE - topE);
        ClampToLimits(ref _x, ref _y, ref _width, ref _height);
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
    /// Projects the rectangle onto integer pixel coordinates (X / Y may be negative for an
    /// outpaint). Returns <c>false</c> when there is no usable (non-empty) rectangle. The
    /// stored rectangle is already limited, so this only rounds.
    /// </summary>
    public bool TryGetPixelRect(out int x, out int y, out int width, out int height)
    {
        x = y = width = height = 0;
        if (!HasRect || !HasImage)
        {
            return false;
        }

        var left = (int)Math.Round(_x);
        var top = (int)Math.Round(_y);
        var right = (int)Math.Round(_x + _width);
        var bottom = (int)Math.Round(_y + _height);
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
        _width = Math.Clamp(_width, MinSize, MaxWidth);
        _height = Math.Clamp(_height, MinSize, MaxHeight);
        ClampToLimits(ref _x, ref _y, ref _width, ref _height);
    }

    /// <summary>Largest allowed output width / height (D2).</summary>
    private double MaxWidth => Math.Max(MinSize, _imageWidth * MaxExpandFactor);

    private double MaxHeight => Math.Max(MinSize, _imageHeight * MaxExpandFactor);

    /// <summary>
    /// Limits a rectangle to the outpaint budget (D2): edges up to
    /// <see cref="MaxExpandFactor"/>× the image, area up to <see cref="MaxPixelCount"/>, and
    /// a positive-area overlap with the source. Position is limited only for a non-empty
    /// rectangle so a zero-size build candidate is left untouched.
    ///
    /// <para><b>9C.4-B-P2</b>: the pixel cap applies <b>only to an outpaint</b> (a rectangle
    /// that reaches outside the image). A pure inner crop keeps the source resolution, even
    /// when the source itself is larger than <see cref="MaxPixelCount"/>.</para>
    /// </summary>
    private void ClampToLimits(ref double x, ref double y, ref double width, ref double height)
    {
        if (!HasImage)
        {
            return;
        }

        width = Math.Clamp(width, 0, MaxWidth);
        height = Math.Clamp(height, 0, MaxHeight);

        if (width > 0 && height > 0 && width * height > MaxPixelCount
            && IsOutpaint(x, y, width, height))
        {
            var scale = Math.Sqrt(MaxPixelCount / (width * height));
            width *= scale;
            height *= scale;
        }

        if (width > 0 && height > 0)
        {
            // Keep >= 1px overlap with the source and the right/bottom edge within budget.
            var minX = Math.Max(-_imageWidth, -width + 1);
            var maxX = Math.Min(_imageWidth - 1, MaxWidth - width);
            x = Math.Clamp(x, minX, Math.Max(minX, maxX));

            var minY = Math.Max(-_imageHeight, -height + 1);
            var maxY = Math.Min(_imageHeight - 1, MaxHeight - height);
            y = Math.Clamp(y, minY, Math.Max(minY, maxY));
        }
    }

    /// <summary>
    /// True when the rectangle reaches outside the image (an outpaint). A tolerance absorbs
    /// floating-point noise so a crop that sits exactly on the edge counts as an inner crop.
    /// </summary>
    private bool IsOutpaint(double x, double y, double width, double height)
        => x < -BoundaryEpsilon
           || y < -BoundaryEpsilon
           || x + width > _imageWidth + BoundaryEpsilon
           || y + height > _imageHeight + BoundaryEpsilon;
}
