using System;
using Avalonia.Controls;
using ZivAiEditor.Contracts.Models;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Resolution tier picker (Step 6.5 logic, first UI): a small dropdown offering the
/// model's <see cref="ResolutionTier"/> values (Fast / Balanced / HighQuality / Custom)
/// with their long edges read from the <see cref="ModelProfile"/>. It only holds the
/// selection for now; wiring the chosen tier into the edit request is a follow-up.
/// </summary>
public partial class ResolutionPicker : UserControl
{
    private static readonly ResolutionTier[] Tiers =
    {
        ResolutionTier.Fast,
        ResolutionTier.Balanced,
        ResolutionTier.HighQuality,
        ResolutionTier.Custom,
    };

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

    /// <summary>Active tier; defaults to <see cref="ResolutionTier.Balanced"/>.</summary>
    public ResolutionTier Tier { get; private set; } = ResolutionTier.Balanced;

    /// <summary>Binds the model profile used for tier labels (long edges).</summary>
    public void Attach(ModelProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
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
        foreach (var tier in Tiers)
        {
            var item = new MenuItem { Header = LabelFor(tier) };
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
            _label.Text = LabelFor(Tier);
        }
    }

    private string LabelFor(ResolutionTier tier)
    {
        if (tier == ResolutionTier.Custom)
        {
            return "自定义";
        }

        var side = _profile is not null && _profile.TierSides.TryGetValue(tier, out var value)
            ? $" {value}"
            : "";
        var name = tier switch
        {
            ResolutionTier.Fast => "快速",
            ResolutionTier.HighQuality => "高质",
            _ => "均衡",
        };
        return name + side;
    }
}