using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ZivAiEditor.App.Imaging;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Imaging;
using ZivAiEditor.UI.Editing;
using ZivAiEditor.UI.Imaging;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Large-image preview window (Step 9C.1), opened by clicking an image in the chat
/// stream. Behaviour: fit-to-window by default, wheel zoom anchored at the pointer,
/// Space+left / middle-drag pan (all modes), double-click toggles fit / 100%,
/// Esc exits crop → compare → window.
///
/// The <see cref="PanZoomCanvas"/> is used for rendering only: all zoom / pan state
/// is computed by the pure, unit-tested <see cref="ImageViewModel"/> (ZivAiEditor.UI)
/// and applied back to the canvas. The bitmap is decoded off the UI thread (Z11) and
/// disposed when the window closes (Z9).
/// </summary>
public partial class ImagePreview : Window
{
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(300);
    private const double DragThreshold = 4.0;
    private const double WheelStep = 1.2;

    private readonly ImageViewModel _model = new();
    private readonly ToolStateMachine _tools = new();

    private PanZoomCanvas? _canvas;
    private TextBlock? _emptyLabel;
    private Border? _zoomBadge;
    private TextBlock? _zoomText;
    private Border? _sizeBadge;
    private TextBlock? _sizeText;
    private EditorToolbar? _toolbar;
    private TextBlock? _titleText;
    private Button? _resetView;

    private readonly IImagingService _imaging;
    private readonly IDisplayImageLoader _displayLoader;
    private Bitmap? _bitmap;
    private string? _path;
    private int _generation;

    /// <summary>
    /// The original (full-size) pixel size of the currently shown image, or empty when unknown.
    /// The view-model works in <b>display</b> coordinates (see <see cref="DisplayImage"/>); this is
    /// kept only for the size badge and the crop scale (display → original) so crop output stays at
    /// original resolution.
    /// </summary>
    private PixelSize _originalPixelSize;

    /// <summary>Last tool seen by <see cref="OnToolsChanged"/>, so a switch can re-fit once (B3).</summary>
    private ToolMode _lastToolMode;

    /// <summary>
    /// Designer / runtime-loader only (Avalonia requires a public parameterless ctor for an
    /// <c>x:Class</c> root). Production always uses the injected overload; this never reaches
    /// the injected instance path.
    /// </summary>
    public ImagePreview()
        : this(new ImagingService(), new DisplayImageLoader())
    {
    }

    public ImagePreview(IImagingService imaging)
        : this(imaging, new DisplayImageLoader())
    {
    }

    public ImagePreview(IImagingService imaging, IDisplayImageLoader displayLoader)
    {
        _imaging = imaging ?? throw new ArgumentNullException(nameof(imaging));
        _displayLoader = displayLoader ?? throw new ArgumentNullException(nameof(displayLoader));
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
    /// <paramref name="force"/> is set, which reloads the path to pick up content changed
    /// in place (e.g. an overwritten temp file).
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
        // Decode / downsample off the UI thread (Z11); the continuation resumes on the UI thread.
        // The single coordinate space is the DISPLAY bitmap size (≤2.5K): the model, renderer and
        // overlays all use it, so there is no second (original-size) coordinate system. The
        // original size is kept only for the badge and the crop scale.
        var display = await _displayLoader.LoadDisplayAsync(path);

        if (generation != _generation)
        {
            display?.Bitmap.Dispose();
            return;
        }

        if (display is null)
        {
            ShowEmpty("图像加载失败");
            return;
        }

        var bitmap = display.Bitmap;
        var shown = display.DisplayPixelSize;
        _originalPixelSize = display.OriginalPixelSize;
        _bitmap = bitmap;
        if (_canvas is not null)
        {
            _canvas.SourceSize = new Size(shown.Width, shown.Height);
            _canvas.Image = bitmap;
        }

        _model.SetViewport(ViewportWidth(), ViewportHeight());
        _model.SetImage(shown.Width, shown.Height);
        ApplyModel();
        RefreshCropBounds();
        RefreshMaskCanvas();

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
            // The badge reports the real image size, not the display proxy's.
            var original = display.OriginalPixelSize;
            _sizeText.Text = $"{original.Width} × {original.Height}";
        }

        if (_sizeBadge is not null)
        {
            _sizeBadge.IsVisible = true;
        }

        _tools.NotifyImageChanged(true);
    }

    private void Init()
    {
        _canvas = this.FindControl<PanZoomCanvas>("PART_ImageCanvas");
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

        // Chrome behaviour is applied by the shell facade after construction
        // (module-boundary migration step 5): MainWindow calls ShellService.ApplyChrome(preview).

        _toolbar = this.FindControl<EditorToolbar>("PART_Toolbar");
        if (_toolbar is not null)
        {
            _toolbar.Attach(_tools);
        }

        InitCrop();
        InitMask();
        InitPointerHandlers();

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

        if (_canvas is not null)
        {
            ((AvaloniaObject)_canvas).PropertyChanged += OnCanvasPropertyChanged;
        }

        Loaded += (_, _) =>
        {
            _model.SetViewport(ViewportWidth(), ViewportHeight());
            ApplyModel();
        };

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        // Losing focus while Space is held must not leave the pan modifier stuck on.
        Deactivated += (_, _) => _spacePan = false;
        Closed += (_, _) => Cleanup();

        ShowEmpty("暂无图像");
    }

    private double ViewportWidth() => _canvas?.Viewport.Width ?? 0;

    private double ViewportHeight() => _canvas?.Viewport.Height ?? 0;

    /// <summary>
    /// The scale from the original image to the display space: <c>display / original</c> (≤ 1 when a
    /// proxy is shown, 1 for a small image / unknown original). The single display coordinate space
    /// is <c>display</c>; crop output and user-facing brush / feather values are in original pixels.
    /// </summary>
    private double DisplayScale
    {
        get
        {
            var original = _originalPixelSize.Width;
            var shown = _model.ImageWidth;
            return original > 0 && shown > 0 ? shown / original : 1.0;
        }
    }

    private void OnViewportChanged()
    {
        if (_canvas is null)
        {
            return;
        }

        var vw = _canvas.Viewport.Width;
        var vh = _canvas.Viewport.Height;
        if (Math.Abs(vw - _model.ViewportWidth) < 0.5 && Math.Abs(vh - _model.ViewportHeight) < 0.5)
        {
            return;
        }

        _model.SetViewport(vw, vh);
        ApplyModel();
    }

    private void OnCanvasPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
        {
            OnViewportChanged();
        }
    }

    /// <summary>Pushes the model state onto the renderer (Z11: called on the UI thread).</summary>
    private void ApplyModel()
    {
        if (_canvas is null)
        {
            return;
        }

        _canvas.Zoom = _model.ZoomPercent;
        _canvas.Offset = new Vector(_model.OffsetX, _model.OffsetY);
        _canvas.InvalidateVisual();
        UpdateZoomBadge();
        _overlay?.InvalidateVisual();
        _cropOverlay?.InvalidateVisual();
        _maskOverlay?.InvalidateVisual();
    }

    private void UpdateZoomBadge()
    {
        if (_zoomText is null)
        {
            return;
        }

        _zoomText.Text = _model.IsAtFit ? $"适配 {_model.ZoomPercent}%" : $"{_model.ZoomPercent}%";
    }

    /// <summary>
    /// Reacts to tool-state changes: cursor, right-slot button enablement and (B3) a
    /// standard fit on every tool <b>switch</b> so each mode starts from a known view.
    /// </summary>
    private void OnToolsChanged()
    {
        UpdateCursor();

        if (_resetView is not null)
        {
            _resetView.IsEnabled = _tools.HasImage;
        }

        if (_tools.CurrentTool != _lastToolMode)
        {
            _lastToolMode = _tools.CurrentTool;
            if (_model.HasImage)
            {
                _model.Fit();
                ApplyModel();
            }
        }
    }

    private void UpdateCursor()
    {
        if (_canvas is null)
        {
            return;
        }

        // R2: the mask tools show ONLY the self-drawn brush circle (MaskOverlay), so the system
        // cursor is hidden for them. Crop keeps Cross (its rectangle needs a precise pointer).
        var type = _tools.CurrentTool switch
        {
            ToolMode.Crop => StandardCursorType.Cross,
            ToolMode.MaskBrush => StandardCursorType.None,
            ToolMode.Eraser => StandardCursorType.None,
            _ => StandardCursorType.Arrow,
        };

        // Compare mode overrides the tool cursor (the divider can be dragged).
        if (_compareState.IsCompareMode)
        {
            type = StandardCursorType.SizeWestEast;
        }

        _canvas.Cursor = new Cursor(type);
    }

    private void ShowEmpty(string message)
    {
        _originalPixelSize = default;
        _model.ClearImage();
        ResetMask();

        if (_canvas is not null)
        {
            _canvas.Image = null;
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
        if (_canvas is not null)
        {
            _canvas.Image = null;
        }

        _bitmap?.Dispose();
        _bitmap = null;
    }

    private void Cleanup()
    {
        MaskDiagnostics.Log("[close] ImagePreview cleanup");
        DisposeBitmap();
        DisposeParentBitmap();
        ResetMask();
    }
}
