using System;
using Avalonia.Controls;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls.Modes;

/// <summary>
/// Crop-mode chrome (N4): aspect dropdown (top-center) and reset / return / confirm
/// (bottom-center). It is <b>dumb</b> — it raises events and renders state pushed
/// by <see cref="ImagePreview" />; it holds no crop logic. The left slot is an unused
/// placeholder. The panel has no background of its own outside its bars, so the canvas
/// keeps its pointer input everywhere else.
/// </summary>
public partial class CropModePanel : UserControl
{
    private Button? _aspectButton;
    private TextBlock? _aspectLabel;
    private Button? _reset;
    private Button? _return;
    private Button? _confirm;
    private CropAspectMode _aspect = CropAspectMode.Free;

    public CropModePanel()
    {
        InitializeComponent();
        Init();
    }

    /// <summary>Raised when the user confirms the crop (✓).</summary>
    public event EventHandler? ConfirmRequested;

    /// <summary>Raised when the user resets the crop rectangle (bottom reset).</summary>
    public event EventHandler? ResetRequested;

    /// <summary>Raised when the user leaves crop mode (返回).</summary>
    public event EventHandler? ReturnRequested;

    /// <summary>Raised when the user picks an aspect ratio from the dropdown.</summary>
    public event EventHandler<CropAspectMode>? AspectChanged;

    /// <summary>Pushes the current aspect onto the label (no event echo).</summary>
    public void SetAspect(CropAspectMode mode)
    {
        _aspect = mode;
        if (_aspectLabel is not null)
        {
            _aspectLabel.Text = Label(mode);
        }
    }

    /// <summary>Enables / disables every action (panel is only shown while crop is active).</summary>
    public void SetEnabled(bool enabled)
    {
        foreach (var button in new[] { _aspectButton, _reset, _return, _confirm })
        {
            if (button is not null)
            {
                button.IsEnabled = enabled;
            }
        }
    }

    private void Init()
    {
        _aspectButton = this.FindControl<Button>("PART_BtnAspect");
        _aspectLabel = this.FindControl<TextBlock>("PART_AspectLabel");
        _reset = this.FindControl<Button>("PART_BtnReset");
        _return = this.FindControl<Button>("PART_BtnReturn");
        _confirm = this.FindControl<Button>("PART_BtnConfirm");

        if (_reset is not null)
        {
            _reset.Click += (_, _) => ResetRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_return is not null)
        {
            _return.Click += (_, _) => ReturnRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_confirm is not null)
        {
            _confirm.Click += (_, _) => ConfirmRequested?.Invoke(this, EventArgs.Empty);
        }

        BuildAspectMenu();
        SetAspect(_aspect);
    }

    private void BuildAspectMenu()
    {
        if (_aspectButton is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var mode in new[]
                 {
                     CropAspectMode.Free,
                     CropAspectMode.R16x9,
                     CropAspectMode.R9x16,
                     CropAspectMode.R1x1,
                 })
        {
            var item = new MenuItem { Header = Label(mode) };
            var captured = mode;
            item.Click += (_, _) =>
            {
                SetAspect(captured);
                AspectChanged?.Invoke(this, captured);
            };
            flyout.Items.Add(item);
        }

        _aspectButton.Flyout = flyout;
    }

    private static string Label(CropAspectMode mode) => mode switch
    {
        CropAspectMode.R16x9 => "16:9",
        CropAspectMode.R9x16 => "9:16",
        CropAspectMode.R1x1 => "1:1",
        _ => "自由",
    };
}
