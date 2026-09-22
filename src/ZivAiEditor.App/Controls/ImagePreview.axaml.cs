using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UVtools.AvaloniaControls;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Large-image preview window (Step 9C.1), opened by clicking an image in the chat
/// stream. Behaviour: fit-to-window by default, wheel zoom anchored at the pointer,
/// left-drag pan, double-click toggles fit / 100%, Esc closes.
///
/// The <see cref="AdvancedImageBox"/> is used for rendering only: all zoom / pan state
/// is computed by the pure, unit-tested <see cref="ImageViewModel"/> (ZivAiEditor.UI)
/// and applied back to the box. The bitmap is decoded off the UI thread (Z11) and
/// disposed when the window closes (Z9).
/// </summary>
public partial class ImagePreview : Window
{
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(300);
    private const double DragThreshold = 4.0;
    private const double WheelStep = 1.2;

    private readonly ImageViewModel _model = new();

    private AdvancedImageBox? _box;
    private TextBlock? _emptyLabel;
    private Border? _zoomBadge;
    private TextBlock? _zoomText;
    private TextBlock? _titleText;

    private Bitmap? _bitmap;
    private string? _path;
    private int _generation;

    private bool _pressed;
    private bool _dragged;
    private Point _pressPoint;
    private Point _lastPanPoint;
    private DateTime _lastClickAt = DateTime.MinValue;

    public ImagePreview()
    {
        InitializeComponent();
        Init();
    }

    /// <summary>Path of the image currently shown; <c>null</c> when empty.</summary>
    public string? ImagePath => _path;

    /// <summary>Zoom percentage currently applied (diagnostics / tests).</summary>
    public int ZoomPercent => _model.ZoomPercent;

    /// <summary>Loads (or clears) the preview image. A repeat path is ignored.</summary>
    public void LoadImage(string? path)
    {
        if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _path = path;
        var generation = ++_generation;
        DisposeBitmap();

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            ShowEmpty(string.IsNullOrWhiteSpace(path) ? "暂无图像" : "图像不存在");
            return;
        }

        Title = $"ZIV.AI - 大图预览 - {System.IO.Path.GetFileName(path)}";
        if (_titleText is not null)
        {
            _titleText.Text = Title;
        }

        _ = LoadAsync(path, generation);
    }

    private async Task LoadAsync(string path, int generation)
    {
        Bitmap? bitmap = null;
        var failed = false;

        // Decode off the UI thread (Z11); the continuation resumes on the UI thread.
        await Task.Run(() =>
        {
            try
            {
                bitmap = new Bitmap(path);
            }
            catch (Exception ex)
            {
                failed = true;
                System.Diagnostics.Debug.WriteLine($"[preview] {ex.Message}");
            }
        });

        if (generation != _generation)
        {
            bitmap?.Dispose();
            return;
        }

        if (failed || bitmap is null)
        {
            ShowEmpty("图像加载失败");
            return;
        }

        _bitmap = bitmap;
        if (_box is not null)
        {
            _box.Image = bitmap;
        }

        _model.SetViewport(ViewportWidth(), ViewportHeight());
        _model.SetImage(bitmap.Size.Width, bitmap.Size.Height);
        ApplyModel();

        if (_emptyLabel is not null)
        {
            _emptyLabel.IsVisible = false;
        }

        if (_zoomBadge is not null)
        {
            _zoomBadge.IsVisible = true;
        }
    }

    private void Init()
    {
        _box = this.FindControl<AdvancedImageBox>("PART_ImageBox");
        _emptyLabel = this.FindControl<TextBlock>("PART_Empty");
        _zoomBadge = this.FindControl<Border>("PART_ZoomBadge");
        _zoomText = this.FindControl<TextBlock>("PART_ZoomText");
        _titleText = this.FindControl<TextBlock>("PART_TitleText");

        InitChrome();

        if (_box is not null)
        {
            _box.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Bubble, handledEventsToo: true);
            _box.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            _box.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
            _box.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            ((AvaloniaObject)_box).PropertyChanged += OnBoxPropertyChanged;
        }

        Loaded += (_, _) =>
        {
            _model.SetViewport(ViewportWidth(), ViewportHeight());
            ApplyModel();
        };

        KeyDown += OnKeyDown;
        Closed += (_, _) => Cleanup();

        ShowEmpty("暂无图像");
    }

    /// <summary>Wires the self-drawn chrome (same pattern as MainWindow; Z4).</summary>
    private void InitChrome()
    {
        var titleBar = this.FindControl<Border>("PART_TitleBar");
        var btnMinimize = this.FindControl<Button>("PART_BtnMinimize");
        var btnMaximize = this.FindControl<Button>("PART_BtnMaximize");
        var btnClose = this.FindControl<Button>("PART_BtnClose");

        if (titleBar is not null)
        {
            WindowDecorationProperties.SetElementRole(titleBar, WindowDecorationsElementRole.TitleBar);
        }

        SetRole(btnMinimize, WindowDecorationsElementRole.MinimizeButton);
        SetRole(btnMaximize, WindowDecorationsElementRole.MaximizeButton);
        SetRole(btnClose, WindowDecorationsElementRole.CloseButton);

        if (btnMinimize is not null)
        {
            btnMinimize.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        if (btnMaximize is not null)
        {
            btnMaximize.Click += (_, _) => WindowState =
                WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        if (btnClose is not null)
        {
            btnClose.Click += (_, _) => Close();
        }

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMaximizeIcon();
            }
        };

        UpdateMaximizeIcon();
    }

    private static void SetRole(Button? button, WindowDecorationsElementRole role)
    {
        if (button is not null)
        {
            WindowDecorationProperties.SetElementRole(button, role);
        }
    }

    private void UpdateMaximizeIcon()
    {
        var icon = this.FindControl<Avalonia.Controls.Shapes.Path>("PART_IconMaximize");
        if (icon is null)
        {
            return;
        }

        icon.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M0 3H7V10H0Z M3 0H10V7H3Z"
            : "M0 0H10V10H0Z");
    }

    private double ViewportWidth() => _box?.Viewport.Width ?? 0;

    private double ViewportHeight() => _box?.Viewport.Height ?? 0;

    private void OnViewportChanged()
    {
        if (_box is null)
        {
            return;
        }

        var vw = _box.Viewport.Width;
        var vh = _box.Viewport.Height;
        if (Math.Abs(vw - _model.ViewportWidth) < 0.5 && Math.Abs(vh - _model.ViewportHeight) < 0.5)
        {
            return;
        }

        _model.SetViewport(vw, vh);
        ApplyModel();
    }

    private void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
        {
            OnViewportChanged();
        }
    }

    /// <summary>Pushes the model state onto the renderer (Z11: called on the UI thread).</summary>
    private void ApplyModel()
    {
        if (_box is null)
        {
            return;
        }

        _box.Zoom = _model.ZoomPercent;
        _box.Offset = new Vector(_model.OffsetX, _model.OffsetY);
        UpdateZoomBadge();

        // The scroll-bar range follows the new zoom one layout pass later; re-apply
        // the offset then so the anchored position survives the range update.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_box is not null)
                {
                    _box.Offset = new Vector(_model.OffsetX, _model.OffsetY);
                }
            },
            DispatcherPriority.Background);
    }

    private void UpdateZoomBadge()
    {
        if (_zoomText is null)
        {
            return;
        }

        _zoomText.Text = _model.IsAtFit ? $"适配 {_model.ZoomPercent}%" : $"{_model.ZoomPercent}%";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
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

        if (!e.GetCurrentPoint(_box).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = true;
        _dragged = false;
        _pressPoint = e.GetPosition(_box);
        _lastPanPoint = _pressPoint;
        e.Pointer.Capture(_box);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_box is null || !_pressed)
        {
            return;
        }

        var point = e.GetPosition(_box);
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
        if (_box is null || !_pressed)
        {
            return;
        }

        _pressed = false;
        e.Pointer.Capture(null);

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

    private void ShowEmpty(string message)
    {
        _model.ClearImage();

        if (_box is not null)
        {
            _box.Image = null;
        }

        if (_emptyLabel is not null)
        {
            _emptyLabel.Text = message;
            _emptyLabel.IsVisible = true;
        }

        if (_zoomBadge is not null)
        {
            _zoomBadge.IsVisible = false;
        }
    }

    private void DisposeBitmap()
    {
        if (_box is not null)
        {
            _box.Image = null;
        }

        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void Cleanup() => DisposeBitmap();

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
