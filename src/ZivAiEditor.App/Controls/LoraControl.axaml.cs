using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ZivAiEditor.App.Controls;

/// <summary>Editable LoRA state for one command, carried between the control and its owner.</summary>
public sealed class LoraUiState
{
    /// <summary>Whether the LoRA is applied (toggle semantics B: off removes the entry).</summary>
    public bool Enabled { get; init; }

    /// <summary>Strength for both the model and clip sides (0.0 - 2.0).</summary>
    public double Strength { get; init; } = LoraControl.DefaultStrength;

    /// <summary>A registry id (<c>loras.json</c>) or a literal weight path.</summary>
    public string Path { get; init; } = "";

    /// <summary>Optional registry description (display only).</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// Command-level LoRA control (fixed to the input row; shown for a LoRA-capable command — its
/// built-in template declares a LoRA — or any command that currently carries one, including a
/// capability command whose LoRA is turned off, so it can be re-enabled). Mirrors
/// <see cref="ResolutionPicker"/>: a small Button that opens a
/// Flyout. The Flyout content is built eagerly in the constructor so <see cref="CurrentState"/> /
/// <see cref="RequestSave"/> / <see cref="RequestPickFileAsync"/> work without opening the Flyout
/// (which also keeps headless tests simple). The control never touches the backend / IPC; the
/// owner wires <see cref="FilePicker"/> and reacts to <see cref="SaveRequested"/>.
/// </summary>
public partial class LoraControl : UserControl
{
    public const double MinStrength = 0.0;
    public const double MaxStrength = 2.0;
    public const double StrengthStep = 0.05;
    public const double DefaultStrength = 1.0;

    private readonly Button? _button;
    private readonly TextBlock? _label;
    private readonly CheckBox _toggle;
    private readonly Slider _slider;
    private readonly TextBlock _strengthLabel;
    private readonly TextBlock _pathLabel;
    private readonly Button _pickButton;

    private string _path = "";
    private LoraUiState? _openedState;

    public LoraControl()
    {
        InitializeComponent();
        _button = this.FindControl<Button>("PART_Btn");
        _label = this.FindControl<TextBlock>("PART_Label");

        _toggle = new CheckBox { Content = "启用", IsChecked = false, Margin = new Thickness(0, 0, 0, 6) };
        _toggle.IsCheckedChanged += (_, _) => UpdateSummary();

        _slider = new Slider
        {
            Minimum = MinStrength,
            Maximum = MaxStrength,
            Value = DefaultStrength,
            TickFrequency = StrengthStep,
            IsSnapToTickEnabled = true,
            Width = 200,
        };
        _slider.ValueChanged += (_, _) =>
        {
            UpdateStrengthLabel();
            UpdateSummary();
        };

        _strengthLabel = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
        _pathLabel = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        _pickButton = new Button { Content = "选择文件…", Margin = new Thickness(0, 0, 0, 6) };
        _pickButton.Click += (_, _) => _ = RequestPickFileAsync();

        var panel = new StackPanel
        {
            Spacing = 2,
            Width = 240,
            Children =
            {
                _toggle,
                _slider,
                _strengthLabel,
                _pathLabel,
                _pickButton,
                new TextBlock
                {
                    Text = "关闭面板后自动生效",
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0),
                },
            },
        };

        // Auto-save on close (Z-030 复议): snapshot the state when the Flyout opens; on close,
        // raise SaveRequested only when the state actually changed, so a no-op open/close never
        // writes. The owner (MainWindow) performs the write-back + hot reload.
        var flyout = new Flyout { Content = panel };
        flyout.Opened += (_, _) => _openedState = CurrentState;
        flyout.Closed += (_, _) => OnFlyoutClosed();

        if (_button is not null)
        {
            _button.Flyout = flyout;
        }

        UpdateStrengthLabel();
        UpdateSummary();
    }

    /// <summary>Raised when the user saves the panel's current state.</summary>
    public event EventHandler<LoraUiState>? SaveRequested;

    /// <summary>File picker supplied by the owner (absolute path, or <c>null</c> when cancelled).</summary>
    public Func<Task<string?>>? FilePicker { get; set; }

    /// <summary>The current UI state (does not require the Flyout to be open).</summary>
    public LoraUiState CurrentState => new()
    {
        Enabled = _toggle.IsChecked == true,
        Strength = _slider.Value,
        Path = _path,
    };

    /// <summary>Fills the controls from <paramref name="state"/>.</summary>
    public void LoadFrom(LoraUiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _path = state.Path ?? "";
        _toggle.IsChecked = state.Enabled;
        _slider.Value = Clamp(state.Strength);
        _pathLabel.Text = Describe(state.Path, state.DisplayName);
        UpdateStrengthLabel();
        UpdateSummary();
    }

    /// <summary>Raises <see cref="SaveRequested"/> with the current state (programmatic / tests).</summary>
    public void RequestSave() => SaveRequested?.Invoke(this, CurrentState);

    /// <summary>
    /// Flyout-closed handler: raises <see cref="SaveRequested"/> only when the state changed since
    /// the Flyout opened, so opening and closing without edits never triggers a write.
    /// </summary>
    private void OnFlyoutClosed()
    {
        var opened = _openedState;
        _openedState = null;
        if (opened is null || StateEquals(opened, CurrentState))
        {
            return;
        }

        SaveRequested?.Invoke(this, CurrentState);
    }

    /// <summary>Value equality for the change check (DisplayName is display-only, excluded).</summary>
    private static bool StateEquals(LoraUiState a, LoraUiState b)
        => a.Enabled == b.Enabled
           && string.Equals(a.Path ?? "", b.Path ?? "", StringComparison.Ordinal)
           && Math.Abs(a.Strength - b.Strength) < 1e-6;

    /// <summary>Runs the picker and, on a non-null result, sets the path (Pick button / tests).</summary>
    public async Task RequestPickFileAsync()
    {
        if (FilePicker is null)
        {
            return;
        }

        var picked = await FilePicker().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(picked);
        }
        catch (Exception)
        {
            full = picked;
        }

        SetPath(full);
    }

    private void SetPath(string path)
    {
        _path = path;
        _pathLabel.Text = Describe(path, null) + (File.Exists(path) ? "" : "（文件不存在）");
        _toggle.IsChecked = true;
        UpdateSummary();
    }

    private void UpdateStrengthLabel()
        => _strengthLabel.Text = "强度：" + _slider.Value.ToString("0.00", CultureInfo.InvariantCulture);

    private void UpdateSummary()
    {
        if (_label is null)
        {
            return;
        }

        _label.Text = _toggle.IsChecked == true
            ? "LoRA × " + _slider.Value.ToString("0.00", CultureInfo.InvariantCulture)
            : "LoRA 关";
    }

    private static double Clamp(double value)
        => value < MinStrength ? MinStrength : value > MaxStrength ? MaxStrength : value;

    private static string Describe(string? path, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "（未设置权重）";
        }

        return string.IsNullOrWhiteSpace(displayName) ? path : path + " — " + displayName;
    }
}
