using System.Collections.Generic;
using ZivAiEditor.Contracts.Models;
using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 8-3: the pure resolution-tier options helper (tier set from <c>TierSides</c>, labels
/// from <c>TierLabels</c>, tightened design-time / production fallbacks). No GPU / Avalonia.
/// </summary>
public class ResolutionTierOptionsTests
{
    private static ModelProfile Profile(
        IEnumerable<ResolutionTier> tiers,
        IReadOnlyDictionary<ResolutionTier, string>? labels = null)
    {
        var sides = new Dictionary<ResolutionTier, int>();
        var side = 1000;
        foreach (var tier in tiers)
        {
            sides[tier] = side++;
        }

        return new ModelProfile
        {
            ModelId = "m",
            TierSides = sides,
            TierLabels = labels ?? new Dictionary<ResolutionTier, string>(),
        };
    }

    [Fact]
    public void Options_Orders_By_Enum_And_Appends_Custom()
    {
        var profile = Profile(new[]
        {
            ResolutionTier.HighQuality,
            ResolutionTier.Fast,
            ResolutionTier.Balanced,
        });

        Assert.Equal(
            new[]
            {
                ResolutionTier.Fast,
                ResolutionTier.Balanced,
                ResolutionTier.HighQuality,
                ResolutionTier.Custom,
            },
            ResolutionTierOptions.Options(profile));
    }

    [Fact]
    public void Options_Omits_Tiers_Without_A_Side()
    {
        var profile = Profile(new[] { ResolutionTier.Fast, ResolutionTier.HighQuality });

        Assert.Equal(
            new[] { ResolutionTier.Fast, ResolutionTier.HighQuality, ResolutionTier.Custom },
            ResolutionTierOptions.Options(profile));
    }

    [Fact]
    public void DefaultTier_Prefers_Balanced_Else_First_NonCustom()
    {
        Assert.Equal(
            ResolutionTier.Balanced,
            ResolutionTierOptions.DefaultTier(Profile(new[]
            {
                ResolutionTier.Fast,
                ResolutionTier.Balanced,
                ResolutionTier.HighQuality,
            })));

        Assert.Equal(
            ResolutionTier.HighQuality,
            ResolutionTierOptions.DefaultTier(Profile(new[] { ResolutionTier.HighQuality })));
    }

    [Fact]
    public void Label_Uses_Data_Then_Enum_Name_In_Production()
    {
        var profile = Profile(
            new[] { ResolutionTier.Fast, ResolutionTier.Balanced },
            new Dictionary<ResolutionTier, string> { [ResolutionTier.Fast] = "极速" });

        Assert.Equal("极速", ResolutionTierOptions.Label(profile, ResolutionTier.Fast));
        // Missing label in production -> enum name (visible config error), not a silent fallback.
        Assert.Equal("Balanced", ResolutionTierOptions.Label(profile, ResolutionTier.Balanced));
    }

    [Fact]
    public void Label_DesignTime_Falls_Back_To_Chinese()
    {
        Assert.Equal("快速", ResolutionTierOptions.Label(null, ResolutionTier.Fast));
        Assert.Equal("均衡", ResolutionTierOptions.Label(null, ResolutionTier.Balanced));
        Assert.Equal("高质", ResolutionTierOptions.Label(null, ResolutionTier.HighQuality));
        Assert.Equal("自定义", ResolutionTierOptions.Label(null, ResolutionTier.Custom));
    }

    [Fact]
    public void Display_Appends_Side_Except_Custom()
    {
        var profile = Profile(
            new[] { ResolutionTier.Fast, ResolutionTier.Balanced },
            new Dictionary<ResolutionTier, string>
            {
                [ResolutionTier.Fast] = "极速",
                [ResolutionTier.Balanced] = "均衡",
            });

        Assert.Equal("极速 1000", ResolutionTierOptions.Display(profile, ResolutionTier.Fast));
        Assert.Equal("均衡 1001", ResolutionTierOptions.Display(profile, ResolutionTier.Balanced));
        Assert.Equal("自定义", ResolutionTierOptions.Display(profile, ResolutionTier.Custom));
    }
}
