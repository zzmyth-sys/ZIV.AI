using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls.Modes;

/// <summary>
/// Mask-mode chrome (N4): brush / eraser / clear / undo (top-center), brush-size and feather
/// sliders (left, icons only), reset / return / confirm (bottom-center). It is <b>dumb</b> —
/// it raises events and renders state pushed by <see cref="ImagePreview" /> / the mask
/// state; the sliders are pushed with <see cref="SetBrushSize" /> / <see cref="SetFeather" />
/// without an echo event. The panel has no background outside its bars, so the canvas keeps
/// its pointer input everywhere else.
/// </summary>
public partial class MaskModePanel : UserControl
{
    private ToggleButton? _brush;
    private ToggleButton? _eraser;
    private Button? _clear;
    private Button? _undo;
    private Slider? _brushSlider;
    private Slider? _featherSlider;
    private bool _suppress;

    public MaskModePanel()
    {
        InitializeComponent();
        Init();
    }

    /// <summary>Raised when the user selects the brush tool.</summary>
    public event EventHandler? BrushChanged;

    /// <summary>Raised when the user selects the eraser tool.</summary>
    public event EventHandler? EraserChanged;

    /// <summary>Raised when the user asks to clear the mask.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>Raised when the user asks to undo the last mask change.</summary>
    public event EventHandler? UndoRequested;

    /// <summary>Raised when the user picks a brush diameter (image pixels).</summary>
    public event EventHandler<int>? BrushSizeChanged;

    /// <summary>Raised when the user picks a feather radius (image pixels).</summary>
    public event EventHandler<int>? FeatherChanged;

    /// <summary>Raised when the user resets (clears) the mask.</summary>
    public event EventHandler? ResetRequested;

    /// <summary>Raised when the user returns to the plain preview (返回).</summary>
    public event EventHandler? ReturnRequested;

    /// <summary>Raised when the user confirms the mask edit (✓).</summary>
    public event EventHandler? ConfirmRequested;

    /// <summary>Current brush diameter shown by the slider.</summary>
    public int BrushSize => (int)Math.Round(_brushSlider?.Value ?? 40);

    /// <summary>Current feather radius shown by the slider.</summary>
    public int Feather => (int)Math.Round(_featherSlider?.Value ?? 0);

    /// <summary>Pushes the brush slider without raising <see cref="BrushSizeChanged"/>.</summary>
    public void SetBrushSize(int diameterPx)
    {
        _suppress = true;
        try
        {
            if (_brushSlider is not null)
            {
                _brushSlider.Value = diameterPx;
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>Pushes the feather slider without raising <see cref="FeatherChanged"/>.</summary>
    public void SetFeather(int featherPx)
    {
        _suppress = true;
        try
        {
            if (_featherSlider is not null)
            {
                _featherSlider.Value = featherPx;
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>Pushes the active tool + mask action enablement (state-driven, no events).</summary>
    public void SetToolState(ToolMode tool, bool canUndo, bool canClearMask)
    {
        if (_brush is not null)
        {
            _brush.IsChecked = tool == ToolMode.MaskBrush;
        }

        if (_eraser is not null)
        {
            _eraser.IsChecked = tool == ToolMode.Eraser;
        }

        if (_undo is not null)
        {
            _undo.IsEnabled = canUndo;
        }

        if (_clear is not null)
        {
            _clear.IsEnabled = canClearMask;
        }
    }

    private void Init()
    {
        _brush = this.FindControl<ToggleButton>("PART_BtnBrush");
        _eraser = this.FindControl<ToggleButton>("PART_BtnEraser");
        _clear = this.FindControl<Button>("PART_BtnClear");
        _undo = this.FindControl<Button>("PART_BtnUndo");
        _brushSlider = this.FindControl<Slider>("PART_BrushSlider");
        _featherSlider = this.FindControl<Slider>("PART_FeatherSlider");

        if (_brush is not null)
        {
            _brush.Click += (_, _) => BrushChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_eraser is not null)
        {
            _eraser.Click += (_, _) => EraserChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_clear is not null)
        {
            _clear.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_undo is not null)
        {
            _undo.Click += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
        }

        if (this.FindControl<Button>("PART_BtnReset") is { } reset)
        {
            reset.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);
        }

        if (this.FindControl<Button>("PART_BtnReturn") is { } back)
        {
            back.Click += (_, _) => ReturnRequested?.Invoke(this, EventArgs.Empty);
        }

        if (this.FindControl<Button>("PART_BtnConfirm") is { } confirm)
        {
            confirm.Click += (_, _) => ConfirmRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_brushSlider is not null)
        {
            ToolTip.SetTip(_brushSlider, BrushSize.ToString());
            _brushSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                {
                    // No persistent text: the current value is shown on hover.
                    ToolTip.SetTip(_brushSlider, BrushSize.ToString());
                    if (!_suppress)
                    {
                        BrushSizeChanged?.Invoke(this, BrushSize);
                    }
                }
            };
        }

        if (_featherSlider is not null)
        {
            ToolTip.SetTip(_featherSlider, Feather.ToString());
            _featherSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                {
                    ToolTip.SetTip(_featherSlider, Feather.ToString());
                    if (!_suppress)
                    {
                        FeatherChanged?.Invoke(this, Feather);
                    }
                }
            };
        }
    }
}
