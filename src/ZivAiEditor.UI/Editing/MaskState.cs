namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Pure mask-editing state for the preview window (Step 9C.7). The mask is a plain
/// <c>byte[]</c> buffer whose values are <see cref="On"/> (255) or <see cref="Off"/> (0);
/// it carries <b>no</b> Avalonia / Skia dependency, so the brush, eraser, interpolation,
/// boundary clamping and undo stack are unit-testable (Z3/Z6). Skia is used only by the
/// App-layer exporter for file IO; the on-screen overlay is a display-only bitmap.
///
/// <para>Coordinates are the current <b>pipeline</b> image's pixel space (crop-result
/// canvas), matching <c>MaskSpec</c>. The brush stamps a filled circle of
/// <see cref="BrushDiameter"/> pixels; strokes interpolate between pointer samples so
/// there are no gaps (R5). Undo keeps whole-buffer snapshots, bounded at
/// <see cref="MaxUndo"/> (D4/R4).</para>
/// </summary>
public sealed class MaskState
{
    /// <summary>Lowest brush / eraser diameter, in image pixels (slider clamp).</summary>
    public const int MinBrushDiameter = 5;

    /// <summary>Highest brush / eraser diameter, in image pixels (slider clamp).</summary>
    public const int MaxBrushDiameter = 200;

    /// <summary>
    /// Highest feather radius, in image pixels (slider / setter clamp). E8 lowered this from
    /// 25 to 15: GPU testing (<c>mask_feather_result.md</c>) showed a large feather weakens the
    /// edit strength of the mask core, so the product cap is kept conservative.
    /// </summary>
    public const int MaxFeatherPx = 15;

    /// <summary>Maximum number of undo snapshots kept (D4/R4).</summary>
    public const int MaxUndo = 20;

    /// <summary>Mask value of a painted pixel.</summary>
    public const byte On = 255;

    /// <summary>Mask value of an unpainted pixel.</summary>
    public const byte Off = 0;

    private byte[]? _pixels;
    private int _width;
    private int _height;
    private bool _hasContent;
    private int _brushDiameter = 40;
    private int _featherPx;

    private readonly List<byte[]> _undo = new();

    private byte[]? _pendingSnapshot;
    private bool _strokeActive;
    private bool _strokeChanged;
    private byte _strokeValue = On;
    private double _lastX;
    private double _lastY;

    // Dirty region since the last TakeDirtyRegion(): allows the overlay to patch only the
    // touched rectangle during a stroke instead of rebuilding the whole bitmap (R1).
    private bool _dirtyAll;
    private bool _hasDirtyRegion;
    private int _dirtyMinX;
    private int _dirtyMinY;
    private int _dirtyMaxX;
    private int _dirtyMaxY;

    /// <summary>
    /// Raised on <b>every</b> mutation, including the intermediate steps of an active stroke
    /// (<see cref="BeginStroke"/> / <see cref="ContinueStroke"/>). Consumers that need live
    /// feedback (e.g. the overlay) use it to repaint the changed region only.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when a mutation produces a <b>durable</b> state (stroke end / clear / undo /
    /// canvas (re)load). Only these need a full redraw and a PNG export, so an active stroke
    /// never triggers the expensive downstream work (R1: one commit per stroke).
    /// <see cref="Changed"/> is also raised immediately before this.
    /// </summary>
    public event EventHandler? Committed;

    /// <summary>Canvas width in pipeline-image pixels; 0 when no canvas is allocated.</summary>
    public int Width => _width;

    /// <summary>Canvas height in pipeline-image pixels; 0 when no canvas is allocated.</summary>
    public int Height => _height;

    /// <summary>True when a canvas with a non-zero size is allocated.</summary>
    public bool HasImage => _width > 0 && _height > 0;

    /// <summary>True when at least one pixel is painted (value <see cref="On"/>).</summary>
    public bool HasContent => _hasContent;

    /// <summary>True when there is a snapshot to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when a mask exists and can be cleared.</summary>
    public bool CanClear => _hasContent;

    /// <summary>
    /// Brush / eraser diameter in image pixels. Clamped to
    /// <c>[<see cref="MinBrushDiameter"/>, <see cref="MaxBrushDiameter"/>]</c> so a stray
    /// slider value can never degenerate the stamp.
    /// </summary>
    public int BrushDiameter
    {
        get => _brushDiameter;
        set => _brushDiameter = Math.Clamp(value, MinBrushDiameter, MaxBrushDiameter);
    }

    /// <summary>
    /// Feather radius in image pixels applied only for display / export (the live buffer
    /// stays 0 / 255). Clamped to <c>[0, <see cref="MaxFeatherPx"/>]</c>.
    /// </summary>
    public int FeatherPx
    {
        get => _featherPx;
        set => _featherPx = Math.Clamp(value, 0, MaxFeatherPx);
    }

    /// <summary>True while a stroke is in progress (between begin and end).</summary>
    public bool IsStrokeActive => _strokeActive;

    /// <summary>
    /// (Re)allocates the canvas, clearing the buffer and the undo stack. A zero / negative
    /// size yields an empty canvas. Raises <see cref="Changed"/>.
    /// </summary>
    public void SetCanvas(int width, int height)
    {
        Allocate(width, height);
        RaiseCommitted();
    }

    /// <summary>
    /// Sizes the canvas like <see cref="SetCanvas"/> then copies <paramref name="pixels"/>
    /// into it (up to the smaller length) and recomputes <see cref="HasContent"/>. The undo
    /// stack is reset. Raises <see cref="Changed"/>.
    /// </summary>
    public void LoadFrom(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        Allocate(width, height);
        if (_pixels is not null)
        {
            Array.Copy(pixels, _pixels, Math.Min(pixels.Length, _pixels.Length));
        }

        _hasContent = ComputeHasContent();
        RaiseCommitted();
    }

    /// <summary>
    /// Clears every pixel. When there is content the current buffer is pushed onto the undo
    /// stack first, so a clear can be undone. A no-op on an empty mask. Raises <see cref="Committed"/>.
    /// </summary>
    public void Clear()
    {
        if (!HasImage || !_hasContent || _pixels is null)
        {
            return;
        }

        PushUndo((byte[])_pixels.Clone());
        Array.Clear(_pixels, 0, _pixels.Length);
        _hasContent = false;
        MarkDirtyAll();
        RaiseCommitted();
    }

    /// <summary>
    /// Restores the most recent snapshot (pushed by a completed stroke or a clear) and
    /// recomputes <see cref="HasContent"/>. A no-op when the stack is empty. Raises
    /// <see cref="Committed"/>.
    /// </summary>
    public void Undo()
    {
        if (_undo.Count == 0 || _pixels is null)
        {
            return;
        }

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        if (snapshot.Length == _pixels.Length)
        {
            Array.Copy(snapshot, _pixels, snapshot.Length);
        }

        _hasContent = ComputeHasContent();
        MarkDirtyAll();
        RaiseCommitted();
    }

    /// <summary>
    /// Starts a stroke at an image-space point: takes a snapshot for a possible undo and
    /// stamps the first brush circle. Returns <c>false</c> when no canvas is allocated.
    /// </summary>
    public bool BeginStroke(double x, double y, bool erase)
    {
        if (!HasImage || _pixels is null)
        {
            return false;
        }

        _pendingSnapshot = (byte[])_pixels.Clone();
        _strokeValue = erase ? Off : On;
        _strokeActive = true;
        _strokeChanged = StampCircle(x, y, _strokeValue);
        if (_strokeChanged && _strokeValue == On)
        {
            _hasContent = true;
        }

        _lastX = x;
        _lastY = y;
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Extends the active stroke to a new image-space point, stamping brush circles along
    /// the segment so there are no gaps between pointer samples (R5). A no-op when no
    /// stroke is active. Raises <see cref="Changed"/>.
    /// </summary>
    public void ContinueStroke(double x, double y)
    {
        if (!_strokeActive)
        {
            return;
        }

        StampSegment(_lastX, _lastY, x, y, _strokeValue);
        _lastX = x;
        _lastY = y;
        if (_strokeChanged && _strokeValue == On)
        {
            _hasContent = true;
        }

        RaiseChanged();
    }

    /// <summary>
    /// Ends the active stroke. The snapshot is committed to the undo stack only when the
    /// stroke changed at least one pixel; <see cref="HasContent"/> is recomputed. A no-op
    /// when no stroke is active. Raises <see cref="Committed"/> (the single durable event per
    /// stroke — R1).
    /// </summary>
    public void EndStroke()
    {
        if (!_strokeActive)
        {
            return;
        }

        _strokeActive = false;
        _hasContent = ComputeHasContent();
        if (_strokeChanged && _pendingSnapshot is not null)
        {
            PushUndo(_pendingSnapshot);
        }

        _pendingSnapshot = null;
        _strokeChanged = false;
        RaiseCommitted();
    }

    /// <summary>
    /// A fresh copy of the mask buffer (empty when no canvas). Used for the off-thread
    /// export so the UI thread can keep mutating the live buffer (D3).
    /// </summary>
    public byte[] CopyPixels() => _pixels is null ? Array.Empty<byte>() : (byte[])_pixels.Clone();

    /// <summary>
    /// Copies one clipped rectangle out of the buffer (row-major, <paramref name="width"/>×
    /// <paramref name="height"/>). Out-of-range / empty requests return an appropriately sized
    /// zero buffer, so the overlay can patch a region without cloning the whole mask (R1).
    /// </summary>
    public byte[] CopyRegion(int x, int y, int width, int height)
    {
        var region = new byte[Math.Max(0, width) * Math.Max(0, height)];
        if (_pixels is null || width <= 0 || height <= 0
            || x < 0 || y < 0 || x + width > _width || y + height > _height)
        {
            return region;
        }

        for (var row = 0; row < height; row++)
        {
            Array.Copy(_pixels, (y + row) * _width + x, region, row * width, width);
        }

        return region;
    }

    /// <summary>
    /// Returns and clears the rectangle touched since the last call (a full-canvas signal after
    /// a load / clear / undo). <c>null</c> when nothing changed. Used by the overlay for the
    /// in-stroke incremental repaint (R1).
    /// </summary>
    public (int X, int Y, int Width, int Height)? TakeDirtyRegion()
    {
        if (_dirtyAll)
        {
            _dirtyAll = false;
            _hasDirtyRegion = false;
            return _width > 0 && _height > 0 ? (0, 0, _width, _height) : null;
        }

        if (!_hasDirtyRegion)
        {
            return null;
        }

        var region = (_dirtyMinX, _dirtyMinY, _dirtyMaxX - _dirtyMinX + 1, _dirtyMaxY - _dirtyMinY + 1);
        _hasDirtyRegion = false;
        return region;
    }

    private void Allocate(int width, int height)
    {
        _width = width > 0 ? width : 0;
        _height = height > 0 ? height : 0;
        _pixels = _width > 0 && _height > 0 ? new byte[_width * _height] : Array.Empty<byte>();
        _hasContent = false;
        _undo.Clear();
        _pendingSnapshot = null;
        _strokeActive = false;
        _strokeChanged = false;
        MarkDirtyAll();
    }

    private void PushUndo(byte[] snapshot)
    {
        _undo.Add(snapshot);
        while (_undo.Count > MaxUndo)
        {
            _undo.RemoveAt(0);
        }
    }

    /// <summary>
    /// Stamps brush circles from <paramref name="x0"/>,<paramref name="y0"/> to
    /// <paramref name="x1"/>,<paramref name="y1"/>. The step is
    /// <c>max(1, radius / 2)</c>, so consecutive circles overlap and the stroke has no gaps.
    /// </summary>
    private void StampSegment(double x0, double y0, double x1, double y1, byte value)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var step = Math.Max(1.0, BrushDiameter / 2.0 / 2.0);
        var steps = (int)Math.Ceiling(distance / step);
        if (steps < 1)
        {
            steps = 1;
        }

        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            if (StampCircle(x0 + dx * t, y0 + dy * t, value))
            {
                _strokeChanged = true;
            }
        }
    }

    /// <summary>Stamps one filled circle, clamped to the canvas; returns whether it changed a pixel.</summary>
    private bool StampCircle(double cx, double cy, byte value)
    {
        if (_pixels is null || !HasImage)
        {
            return false;
        }

        var radius = BrushDiameter / 2.0;
        var r2 = radius * radius;
        var minX = Math.Max(0, (int)Math.Floor(cx - radius));
        var maxX = Math.Min(_width - 1, (int)Math.Ceiling(cx + radius));
        var minY = Math.Max(0, (int)Math.Floor(cy - radius));
        var maxY = Math.Min(_height - 1, (int)Math.Ceiling(cy + radius));

        var changed = false;
        for (var y = minY; y <= maxY; y++)
        {
            var dy = y + 0.5 - cy;
            var row = y * _width;
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x + 0.5 - cx;
                if (dx * dx + dy * dy > r2)
                {
                    continue;
                }

                var index = row + x;
                if (_pixels[index] != value)
                {
                    _pixels[index] = value;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            MarkDirtyRegion(minX, minY, maxX, maxY);
        }

        return changed;
    }

    private bool ComputeHasContent()
    {
        if (_pixels is null)
        {
            return false;
        }

        foreach (var value in _pixels)
        {
            if (value != Off)
            {
                return true;
            }
        }

        return false;
    }

    private void MarkDirtyAll() => _dirtyAll = true;

    private void MarkDirtyRegion(int x0, int y0, int x1, int y1)
    {
        if (_dirtyAll)
        {
            return;
        }

        if (!_hasDirtyRegion)
        {
            _hasDirtyRegion = true;
            _dirtyMinX = x0;
            _dirtyMinY = y0;
            _dirtyMaxX = x1;
            _dirtyMaxY = y1;
            return;
        }

        if (x0 < _dirtyMinX) { _dirtyMinX = x0; }
        if (y0 < _dirtyMinY) { _dirtyMinY = y0; }
        if (x1 > _dirtyMaxX) { _dirtyMaxX = x1; }
        if (y1 > _dirtyMaxY) { _dirtyMaxY = y1; }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="Changed"/> then <see cref="Committed"/> (durable mutation).</summary>
    private void RaiseCommitted()
    {
        RaiseChanged();
        Committed?.Invoke(this, EventArgs.Empty);
    }
}
