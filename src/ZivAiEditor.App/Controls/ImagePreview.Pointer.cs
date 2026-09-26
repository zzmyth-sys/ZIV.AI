using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Pointer half of <see cref="ImagePreview"/> (Step 9C.7-B / N3): wheel zoom, the pan
/// (Space+left or middle button, active in every mode including crop), the left-button
/// routing when not panning (crop build / mask draw / divider drag) and the mask hover
/// circle. Split out of the main file to keep each file under the Z8 line budget; the halves
/// share the same partial-class members (fields, the image model, the tool state).
///
/// <para><b>R1 both-buttons rule.</b> A <see cref="PointerArbiter"/> guarantees at most one
/// owner: a mask stroke claims <c>Draw</c>, a pan claims <c>Pan</c>. The first to go down
/// wins; the other is ignored until the first releases, and each release only ends its own
/// mode. A pan is always checked first so it works in crop mode too (N3).</para>
/// </summary>
public partial class ImagePreview
{
    private readonly PointerArbiter _arbiter = new();

    private bool _pressed;
    private bool _dragged;
    private Point _pressPoint;
    private Point _lastPanPoint;
    private DateTime _lastClickAt = DateTime.MinValue;

    /// <summary>True while a pan (middle button or Space+left) owns the pointer (R1/B3).</summary>
    private bool _panDown;

    /// <summary>The button that started the active pan, so only its release ends it (R1/B3).</summary>
    private MouseButton _panButton;

    /// <summary>Registers the pointer handlers on the image canvas. Called from <see cref="Init"/>.</summary>
    private void InitPointerHandlers()
    {
        if (_canvas is null)
        {
            return;
        }

        _canvas.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Bubble, handledEventsToo: true);
        _canvas.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _canvas.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _canvas.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _canvas.AddHandler(PointerExitedEvent, OnPointerExited, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_canvas is null || !_model.HasImage || e.Delta.Y == 0)
        {
            return;
        }

        var point = e.GetPosition(_canvas);
        _model.SetViewport(ViewportWidth(), ViewportHeight());
        _model.ZoomBy(e.Delta.Y > 0 ? WheelStep : 1.0 / WheelStep, point.X, point.Y);
        ApplyModel();
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_canvas is null || !_model.HasImage)
        {
            return;
        }

        var properties = e.GetCurrentPoint(_canvas).Properties;
        var point = e.GetPosition(_canvas);

        // B3: pan claims the pointer in EVERY mode, including crop. The middle button always
        // pans; the left button pans while Space is held. It is checked before the crop
        // branch so a crop drag cannot swallow an active pan.
        var wantsPan = properties.IsMiddleButtonPressed
            || (properties.IsLeftButtonPressed && _spacePan);
        if (wantsPan)
        {
            // R1: at most one owner. A left draw stroke that already owns the surface makes
            // an overlapping press a no-op until it releases.
            if (!_arbiter.TryBeginPan())
            {
                return;
            }

            _panDown = true;
            _panButton = properties.IsMiddleButtonPressed ? MouseButton.Middle : MouseButton.Left;
            _pressed = true;
            _dragged = false;
            _pressPoint = point;
            _lastPanPoint = point;
            e.Pointer.Capture(_canvas);
            return;
        }

        // Left WITHOUT Space in crop mode builds / moves / resizes the selection.
        if (IsCropActive)
        {
            if (properties.IsLeftButtonPressed)
            {
                _pressPoint = point;
                _lastPanPoint = point;
                CropOnPressed(_pressPoint, e);
            }

            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressPoint = point;
        _lastPanPoint = point;

        // R1: a left press starts a draw stroke (mask) or a plain left pan. If a pan already
        // owns the surface, the left press is ignored until it releases.
        if (!_arbiter.TryBeginDraw())
        {
            return;
        }

        // Mask mode stamps the brush / eraser (no pan).
        if (IsMaskActive)
        {
            MaskOnPressed(_pressPoint, e);
            return;
        }

        // In compare mode a press near the divider starts a divider drag instead of a pan.
        if (_compareState.IsCompareMode && IsNearDivider(_pressPoint.X))
        {
            _draggingDivider = true;
            _pressed = true;
            _dragged = false;
            e.Pointer.Capture(_canvas);
            return;
        }

        _pressed = true;
        _dragged = false;
        e.Pointer.Capture(_canvas);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas is null)
        {
            return;
        }

        var point = e.GetPosition(_canvas);

        // Mask hover: the brush circle follows the cursor whenever a brush / eraser tool is
        // active (even with no button down, and even when the mask is empty).
        if (IsMaskActive && _maskOverlay is not null)
        {
            _maskOverlay.SetPointer(point.X, point.Y);
        }

        // Pan (middle / Space+left) applies immediately (no drag threshold) and in every
        // tool mode, including crop (B3). It must run before the crop / mask branches, which
        // would otherwise swallow the move.
        if (_panDown)
        {
            _model.PanBy(point.X - _lastPanPoint.X, point.Y - _lastPanPoint.Y);
            _lastPanPoint = point;
            ApplyModel();
            return;
        }

        if (IsCropActive)
        {
            CropOnMoved(point);
            return;
        }

        if (IsMaskActive)
        {
            if (_maskPointerDown)
            {
                MaskOnMoved(point);
            }

            return;
        }

        if (!_pressed)
        {
            return;
        }

        if (_draggingDivider)
        {
            SetDividerFromViewport(point.X);
            return;
        }

        // A left drag starts panning only past the threshold so a double-click still
        // toggles fit / 100%.
        if (!_dragged && Distance(point, _pressPoint) > DragThreshold)
        {
            _dragged = true;
        }

        if (!_dragged)
        {
            return;
        }

        _model.PanBy(point.X - _lastPanPoint.X, point.Y - _lastPanPoint.Y);
        _lastPanPoint = point;
        ApplyModel();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_canvas is null)
        {
            return;
        }

        var button = e.InitialPressMouseButton;

        // B3: a pan owns the pointer in every mode, so end it before any mode handler. Only
        // the button that started the pan ends it (R1: the other button's release is a no-op).
        if (_panDown)
        {
            if (button == _panButton)
            {
                _panDown = false;
                _pressed = false;
                _dragged = false;
                _arbiter.EndPan();
                e.Pointer.Capture(null);
            }

            return;
        }

        if (IsCropActive)
        {
            CropOnReleased(e);
            return;
        }

        if (button != MouseButton.Left)
        {
            return;
        }

        // R1: a left release only ends a draw stroke / left pan; it must not end a middle pan.
        if (_arbiter.Owner == PointerOwner.Pan)
        {
            return;
        }

        if (IsMaskActive)
        {
            MaskOnReleased(e);
            _arbiter.EndDraw();
            return;
        }

        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        _arbiter.EndDraw();
        e.Pointer.Capture(null);

        if (_draggingDivider)
        {
            _draggingDivider = false;
            return;
        }

        // A drag is a pan; a double click (no drag) toggles fit / 100%.
        if (_dragged)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastClickAt < DoubleClickWindow)
        {
            _lastClickAt = DateTime.MinValue;
            _model.ToggleFitActual();
            ApplyModel();
        }
        else
        {
            _lastClickAt = now;
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _maskOverlay?.ClearPointer();
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}