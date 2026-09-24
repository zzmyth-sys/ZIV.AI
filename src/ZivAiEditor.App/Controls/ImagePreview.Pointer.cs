using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Pointer half of <see cref="ImagePreview"/> (Step 9C.7-B): wheel zoom, the left-button
/// routing (crop → mask draw → divider → pan), the middle-button pan and the mask hover
/// circle. Split out of the main file to keep each file under the Z8 line budget; the halves
/// share the same partial-class members (fields, the image model, the tool state).
///
/// <para><b>R1 both-buttons rule.</b> A <see cref="PointerArbiter"/> guarantees at most one
/// owner: a left press that starts a mask stroke (or a plain left pan) claims <c>Draw</c>, a
/// middle press claims <c>Pan</c>. The first to go down wins; the other is ignored until the
/// first releases, and each release only ends its own mode.</para>
/// </summary>
public partial class ImagePreview
{
    private readonly PointerArbiter _arbiter = new();

    private bool _pressed;
    private bool _dragged;
    private Point _pressPoint;
    private Point _lastPanPoint;
    private DateTime _lastClickAt = DateTime.MinValue;

    /// <summary>True while a middle-button pan owns the pointer (R1).</summary>
    private bool _panMiddleDown;

    /// <summary>Registers the pointer handlers on the image box. Called from <see cref="Init"/>.</summary>
    private void InitPointerHandlers()
    {
        if (_box is null)
        {
            return;
        }

        _box.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Bubble, handledEventsToo: true);
        _box.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _box.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _box.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _box.AddHandler(PointerExitedEvent, OnPointerExited, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_box is null || !_model.HasImage || e.Delta.Y == 0)
        {
            return;
        }

        var point = e.GetPosition(_box);
        _model.SetViewport(ViewportWidth(), ViewportHeight());
        _model.ZoomBy(e.Delta.Y > 0 ? WheelStep : 1.0 / WheelStep, point.X, point.Y);
        ApplyModel();
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_box is null || !_model.HasImage)
        {
            return;
        }

        var properties = e.GetCurrentPoint(_box).Properties;
        var point = e.GetPosition(_box);

        // Crop mode owns the pointer: build / move / resize the selection (no pan). Middle
        // pan is ignored while cropping so it cannot fight the crop drag.
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

        // R1: the middle button claims a pan only when nothing else owns the surface. A left
        // draw stroke that already owns it makes the middle press a no-op until it releases.
        if (properties.IsMiddleButtonPressed)
        {
            if (!_arbiter.TryBeginPan())
            {
                return;
            }

            _panMiddleDown = true;
            _pressed = true;
            _dragged = false;
            _pressPoint = point;
            _lastPanPoint = point;
            e.Pointer.Capture(_box);
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressPoint = point;
        _lastPanPoint = point;

        // R1: a left press starts a draw stroke (mask) or a plain left pan. If a middle pan
        // already owns the surface, the left press is ignored until the middle releases.
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
            e.Pointer.Capture(_box);
            return;
        }

        _pressed = true;
        _dragged = false;
        e.Pointer.Capture(_box);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_box is null)
        {
            return;
        }

        var point = e.GetPosition(_box);

        // Mask hover: the brush circle follows the cursor whenever a brush / eraser tool is
        // active (even with no button down, and even when the mask is empty).
        if (IsMaskActive && _maskOverlay is not null)
        {
            _maskOverlay.SetPointer(point.X, point.Y);
        }

        // Middle pan applies immediately (no drag threshold) and in every tool mode,
        // including mask mode (R1: middle pan is not exclusive with drawing). It must run
        // before the crop / mask branches, which would otherwise swallow the move.
        if (_panMiddleDown)
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
        if (_box is null)
        {
            return;
        }

        var button = e.InitialPressMouseButton;

        if (IsCropActive)
        {
            CropOnReleased(e);
            return;
        }

        // R1: a middle release only ends a middle pan; it must not end a draw stroke.
        if (button == MouseButton.Middle)
        {
            if (!_panMiddleDown)
            {
                return;
            }

            _panMiddleDown = false;
            _pressed = false;
            _dragged = false;
            _arbiter.EndPan();
            e.Pointer.Capture(null);
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