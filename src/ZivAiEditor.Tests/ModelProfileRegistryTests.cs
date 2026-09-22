using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Models;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 6.5 model profile registry tests (no GPU).</summary>
public class ModelProfileRegistryTests
{
    [Fact]
    public void Get_QwenImage21_Returns_Profile()
    {
        var registry = new ModelProfileRegistry();

        var profile = registry.Get(ModelProfileRegistry.QwenImage21Id);

        Assert.NotNull(profile);
        Assert.Equal(ModelProfileRegistry.QwenImage21Id, profile!.ModelId);
    }

    [Fact]
    public void Get_UnknownModel_Returns_Null()
    {
        var registry = new ModelProfileRegistry();

        Assert.Null(registry.Get("no-such-model"));
        Assert.Null(registry.Get(""));
    }

    [Fact]
    public void Default_Is_QwenImage21()
    {
        var registry = new ModelProfileRegistry();

        Assert.Equal(ModelProfileRegistry.QwenImage21Id, registry.Default.ModelId);
        Assert.Contains(registry.All, profile => profile.ModelId == ModelProfileRegistry.QwenImage21Id);
    }

    [Fact]
    public void QwenImage21_Profile_Fields_Are_Correct()
    {
        var profile = new ModelProfileRegistry().Default;

        Assert.Equal("Qwen-Image-2.1", profile.DisplayName);
        Assert.Equal(2048, profile.NativeSide);
        Assert.Equal(2048, profile.SafeMaxSide);
        Assert.Equal(512, profile.MinSide);
        Assert.Equal(16, profile.MultipleOf);
        Assert.Equal(1024, profile.TierSides[ResolutionTier.Fast]);
        Assert.Equal(1536, profile.TierSides[ResolutionTier.Balanced]);
        Assert.Equal(2048, profile.TierSides[ResolutionTier.HighQuality]);
        Assert.Equal(7, profile.Presets.Count);
        Assert.Contains(profile.Presets, preset => preset.Name == "1:1" && preset.Width == 2048 && preset.Height == 2048);
        Assert.Contains(profile.Presets, preset => preset.Name == "16:9" && preset.Width == 2752 && preset.Height == 1536);
    }
}
