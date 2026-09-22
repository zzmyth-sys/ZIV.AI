using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Models;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 6.5 resolution-tier translation tests (no GPU).</summary>
public class ResolutionPolicyTests
{
    private static ModelProfile Profile() => new ModelProfileRegistry().Default;

    [Fact]
    public void FromTier_Fast_Returns_Side1024()
    {
        var policy = ResolutionResolver.FromTier(ResolutionTier.Fast, Profile());

        Assert.Equal(ResolutionMode.Side, policy.Mode);
        Assert.Equal(1024, policy.Side);
    }

    [Fact]
    public void FromTier_Balanced_Returns_Side1536()
    {
        var policy = ResolutionResolver.FromTier(ResolutionTier.Balanced, Profile());

        Assert.Equal(ResolutionMode.Side, policy.Mode);
        Assert.Equal(1536, policy.Side);
    }

    [Fact]
    public void FromTier_HighQuality_Returns_Side2048()
    {
        var policy = ResolutionResolver.FromTier(ResolutionTier.HighQuality, Profile());

        Assert.Equal(ResolutionMode.Side, policy.Mode);
        Assert.Equal(2048, policy.Side);
    }

    [Fact]
    public void FromTier_Custom_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ResolutionResolver.FromTier(ResolutionTier.Custom, Profile()));
    }

    [Fact]
    public void MaxPixels_Is_Set_From_Profile()
    {
        var profile = Profile();

        var policy = ResolutionResolver.FromTier(ResolutionTier.Balanced, profile);

        Assert.Equal(profile.SafeMaxSide * profile.SafeMaxSide, policy.MaxPixels);
    }
}
