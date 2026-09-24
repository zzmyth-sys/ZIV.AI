using System;
using Avalonia.Controls;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Resolution tier picker (Step 6.5 logic, first UI; Step 8-3 data-driven): a small dropdown
/// offering the tiers the attached <see cref="ModelProfile"/> defines, with labels / long edges
/// read from it via <see cref="ResolutionTierOptions"/>. The chosen tier is mapped to a
/// <c>ResolutionPolicy</c> and applied to the session view model
/// (<c>MainWindow.InitChat</c> → <c>ApplyResolution</c>).
/// </summary>
public partial class ResolutionPicker : UserControl
{
    private ModelProfile? _profile;
    private Button? _button;
    private TextBlock? _label;

    public ResolutionPicker()
    {
        InitializeComponent();
        _button = this.FindControl<Button>("PART_Btn");
        _label = this.FindControl<TextBlock>("PART_Label");
        UpdateLabel();
    }

    /// <summary>Raised when the user picks a different tier.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Active tier; defaults to <see cref="ResolutionTier.Balanced"/> until attached.</summary>
    public ResolutionTier Tier { get; private set; } = ResolutionTier.Balanced;

    /// <summary>Binds the model profile that drives the tier set / labels (Step 8-3).</summary>
    public void Attach(ModelProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Tier = ResolutionTierOptions.DefaultTier(_profile);
        BuildMenu();
        UpdateLabel();
    }

    private void BuildMenu()
    {
        if (_button is null || _profile is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var tier in ResolutionTierOptions.Options(_profile))
        {
            var item = new MenuItem { Header = ResolutionTierOptions.Display(_profile, tier) };
            var captured = tier;
            item.Click += (_, _) => SetTier(captured);
            flyout.Items.Add(item);
        }

        _button.Flyout = flyout;
    }

    private void SetTier(ResolutionTier tier)
    {
        if (Tier == tier)
        {
            return;
        }

        Tier = tier;
        UpdateLabel();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateLabel()
    {
        if (_label is not null)
        {
            _label.Text = ResolutionTierOptions.Display(_profile, Tier);
        }
    }
}