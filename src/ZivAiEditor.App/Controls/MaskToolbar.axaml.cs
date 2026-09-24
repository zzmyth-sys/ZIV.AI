using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Floating mask toolbar (Step 9C.7-B): brush-size and feather sliders, shown top-center
/// while a brush / eraser tool is active. It calls back into <see cref="ImagePreview"/> via
/// <see cref="BrushSizeChanged"/> / <see cref="FeatherChanged"/>; the setters let the tool
/// half push a value (e.g. restored from a node's mask) without re-raising the event.
///
/// <para><b>No pointer leak.</b> The panel is hit-test-visible (its sliders need input), and
/// it sits above the image box in the Body Grid. <see cref="ImagePreview"/> registers its
/// pointer handlers on the <c>AdvancedImageBox</c>, so clicks on this panel never reach the
/// image handlers.</para>
/// </summary>
public partial class MaskToolbar : UserControl
{
    private Slider? _brushSlider;
    private Slider? _featherSlider;
    private TextBlock? _brushValue;
    private TextBlock? _featherValue;
    private bool _suppress;

    public MaskToolbar()
    {
        InitializeComponent();
        Init();
    }

    /// <summary>Raised when the user picks a brush diameter (image pixels).</summary>
    public event EventHandler<int>? BrushSizeChanged;

    /// <summary>Raised when the user picks a feather radius (image pixels).</summary>
    public event EventHandler<int>? FeatherChanged;

    /// <summary>Current brush diameter shown by the slider.</summary>
    public int BrushSize => (int)Math.Round(_brushSlider?.Value ?? 40);

    /// <summary>Current feather radius shown by the slider.</summary>
    public int Feather => (int)Math.Round(_featherSlider?.Value ?? 0);

    /// <summary>Sets the brush slider / label without raising <see cref="BrushSizeChanged"/>.</summary>
    public void SetBrushSize(int diameterPx)
    {
        _suppress = true;
        try
        {
            if (_brushSlider is not null)
            {
                _brushSlider.Value = diameterPx;
            }

            UpdateBrushLabel();
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>Sets the feather slider / label without raising <see cref="FeatherChanged"/>.</summary>
    public void SetFeather(int featherPx)
    {
        _suppress = true;
        try
        {
            if (_featherSlider is not null)
            {
                _featherSlider.Value = featherPx;
            }

            UpdateFeatherLabel();
        }
        finally
        {
            _suppress = false;
        }
    }

    private void Init()
    {
        _brushSlider = this.FindControl<Slider>("PART_BrushSlider");
        _featherSlider = this.FindControl<Slider>("PART_FeatherSlider");
        _brushValue = this.FindControl<TextBlock>("PART_BrushValue");
        _featherValue = this.FindControl<TextBlock>("PART_FeatherValue");

        if (_brushSlider is not null)
        {
            _brushSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                {
                    OnBrushChanged();
                }
            };
        }

        if (_featherSlider is not null)
        {
            _featherSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                {
                    OnFeatherChanged();
                }
            };
        }

        UpdateBrushLabel();
        UpdateFeatherLabel();
    }

    private void OnBrushChanged()
    {
        UpdateBrushLabel();
        if (!_suppress)
        {
            BrushSizeChanged?.Invoke(this, BrushSize);
        }
    }

    private void OnFeatherChanged()
    {
        UpdateFeatherLabel();
        if (!_suppress)
        {
            FeatherChanged?.Invoke(this, Feather);
        }
    }

    private void UpdateBrushLabel()
    {
        if (_brushValue is not null)
        {
            _brushValue.Text = BrushSize.ToString();
        }
    }

    private void UpdateFeatherLabel()
    {
        if (_featherValue is not null)
        {
            _featherValue.Text = Feather.ToString();
        }
    }
}