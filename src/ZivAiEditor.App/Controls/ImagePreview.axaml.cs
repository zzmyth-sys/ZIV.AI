using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UVtools.AvaloniaControls;
using ZivAiEditor.UI.Editing;
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
    private readonly ToolStateMachine _tools = new();

    private AdvancedImageBox? _box;
    private TextBlock? _emptyLabel;
    private Border? _zoomBadge;
    private TextBlock? _zoomText;
    private Border? _sizeBadge;
    private TextBlock? _sizeText;
    private ChromeTitleBar? _chrome;
    private EditorToolbar? _toolbar;
    private TextBlock? _titleText;
    private Button? _resetView;

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

    /// <summary>The preview's tool state (diagnostics / tests).</summary>
    public ToolStateMachine ToolState => _tools;

    /// <summary>
    /// Raised when the user asks to save the shown image elsewhere (Step 9C.6-E). The App
    /// layer owns the file picker and the session context, so the window only signals intent.
    /// </summary>
    public event EventHandler? SaveRequested;

    /// <summary>
    /// Loads (or clears) the preview image. A repeat path is ignored unless
    /// <paramref name="force"/> is set — the crop temp file is overwritten in place on
    /// re-crop, so confirming a crop reloads the same path to show the new content.
    /// </summary>
    public void LoadImage(string? path, bool force = false)
    {
        if (!force && string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
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

        var fileName = System.IO.Path.GetFileName(path);
        Title = fileName;
        if (_titleText is not null)
        {
            _titleText.Text = fileName;
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
        RefreshCropBounds();

        if (_emptyLabel is not null)
        {
            _emptyLabel.IsVisible = false;
        }

        if (_zoomBadge is not null)
        {
            _zoomBadge.IsVisible = true;
        }

        if (_sizeText is not null)
        {
            _sizeText.Text = $"{bitmap.Size.Width} × {bitmap.Size.Height}";
        }

        if (_sizeBadge is not null)
        {
            _sizeBadge.IsVisible = true;
        }

        _tools.NotifyImageChanged(true);
    }

    private void Init()
    {
        _box = this.FindControl<AdvancedImageBox>("PART_ImageBox");
        _emptyLabel = this.FindControl<TextBlock>("PART_Empty");
        _zoomBadge = this.FindControl<Border>("PART_ZoomBadge");
        _zoomText = this.FindControl<TextBlock>("PART_ZoomText");
        _sizeBadge = this.FindControl<Border>("PART_SizeBadge");
        _sizeText = this.FindControl<TextBlock>("PART_SizeText");
        _titleText = this.FindControl<TextBlock>("PART_TitleText");
        _overlay = this.FindControl<CompareOverlay>("PART_Compare");
        _overlay?.Attach(_model);
        _compareInfo = this.FindControl<Border>("PART_CompareInfo");
        _compareInfoText = this.FindControl<TextBlock>("PART_CompareInfoText");

        _chrome = this.FindControl<ChromeTitleBar>("PART_Chrome");
        if (_chrome is not null)
        {
            ChromeBehavior.Init(this, _chrome);
        }

        _toolbar = this.FindControl<EditorToolbar>("PART_Toolbar");
        if (_toolbar is not null)
        {
            _toolbar.Attach(_tools);
        }

        InitCrop();

        // Right-slot title-bar buttons (reset view / compare). Marked "User" so the OS
        // treats them as client content inside the caption area.
        _resetView = this.FindControl<Button>("PART_BtnResetView");
        _compare = this.FindControl<ToggleButton>("PART_BtnCompare");
        if (_resetView is not null)
        {
            WindowDecorationProperties.SetElementRole(_resetView, WindowDecorationsElementRole.User);
            _resetView.Click += (_, _) =>
            {
                _model.Fit();
                ApplyModel();
            };
        }

        if (_compare is not null)
        {
            WindowDecorationProperties.SetElementRole(_compare, WindowDecorationsElementRole.User);
            _compare.Click += OnCompareClick;
        }

        if (this.FindControl<Button>("PART_BtnSave") is { } save)
        {
            WindowDecorationProperties.SetElementRole(save, WindowDecorationsElementRole.User);
            save.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        _tools.StateChanged += (_, _) => OnToolsChanged();
        OnToolsChanged();

        _compareState.StateChanged += (_, _) => OnCompareStateChanged();

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
        HideScrollBars();
        _overlay?.InvalidateVisual();
        _cropOverlay?.InvalidateVisual();

        // The scroll-bar range follows the new zoom one layout pass later; re-apply
        // the offset then so the anchored position survives the range update.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_box is not null)
                {
                    _box.Offset = new Vector(_model.OffsetX, _model.OffsetY);
                    HideScrollBars();
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Hides the renderer's own scroll bars (they appear once the image is zoomed past
    /// the viewport). Zoom / pan are driven by <see cref="ImageViewModel"/>, so the bars
    /// are redundant; the control's parts stay alive (its code needs them) but invisible.
    /// </summary>
    private void HideScrollBars()
    {
        if (_box is null)
        {
            return;
        }

        foreach (var bar in _box.GetVisualDescendants().OfType<ScrollBar>())
        {
            bar.Visibility = ScrollBarVisibility.Hidden;
        }
    }

    private void UpdateZoomBadge()
    {
        if (_zoomText is null)
        {
            return;
        }

        _zoomText.Text = _model.IsAtFit ? $"适配 {_model.ZoomPercent}%" : $"{_model.ZoomPercent}%";
    }

    /// <summary>Reacts to tool-state changes: cursor + right-slot button enablement.</summary>
    private void OnToolsChanged()
    {
        UpdateCursor();

        if (_resetView is not null)
        {
            _resetView.IsEnabled = _tools.HasImage;
        }
    }

    private void UpdateCursor()
    {
        if (_box is null)
        {
            return;
        }

        var type = _tools.CurrentTool switch
        {
            ToolMode.Crop => StandardCursorType.Cross,
            ToolMode.MaskBrush => StandardCursorType.Cross,
            ToolMode.Eraser => StandardCursorType.Hand,
            _ => StandardCursorType.Arrow,
        };

        // Compare mode overrides the tool cursor (the divider can be dragged).
        if (_compareState.IsCompareMode)
        {
            type = StandardCursorType.SizeWestEast;
        }

        _box.Cursor = new Cursor(type);
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

        _pressPoint = e.GetPosition(_box);
        _lastPanPoint = _pressPoint;

        // Crop mode owns the pointer: build / move / resize the selection (no pan).
        if (IsCropActive)
        {
            CropOnPressed(_pressPoint, e);
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

        if (IsCropActive)
        {
            CropOnMoved(point);
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

        if (IsCropActive)
        {
            CropOnReleased(e);
            return;
        }

        if (!_pressed)
        {
            return;
        }

        _pressed = false;
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

        if (_sizeBadge is not null)
        {
            _sizeBadge.IsVisible = false;
        }

        _tools.NotifyImageChanged(false);

        // No image → nothing to compare; drop the reference too.
        _parentPath = null;
        _parentGeneration++;
        DisposeParentBitmap();
        _compareState.Reset();
        UpdateCompareButton();
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

    private void Cleanup()
    {
        DisposeBitmap();
        DisposeParentBitmap();
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
