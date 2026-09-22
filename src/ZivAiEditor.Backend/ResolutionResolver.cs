using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Models;

namespace ZivAiEditor.Backend;

/// <summary>
/// Translates a user-facing <see cref="ResolutionTier"/> into an absolute
/// <see cref="ResolutionPolicy"/> using a <see cref="ModelProfile"/> (the C#
/// layer owns the tier → number translation; Python is stateless — Z23).
///
/// Only the side-based tier mapping lives here. Outpaint takes an Explicit
/// policy directly from its caller; upscale (Scale mode) remains a Step 7.5
/// candidate — no UpscaleResolver / OutpaintResolver (ARCHITECTURE.md §11).
/// </summary>
public static class ResolutionResolver
{
    public static ResolutionPolicy FromTier(ResolutionTier tier, ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (tier == ResolutionTier.Custom)
        {
            throw new ArgumentException(
                "The Custom tier has no profile value; build an explicit ResolutionPolicy instead.",
                nameof(tier));
        }

        if (!profile.TierSides.TryGetValue(tier, out var side))
        {
            throw new ArgumentException(
                $"Model profile '{profile.ModelId}' does not define a side for tier '{tier}'.",
                nameof(tier));
        }

        return new ResolutionPolicy
        {
            Mode = ResolutionMode.Side,
            Side = side,
            // Step 7 MaxPixels fix: read the profile's pixel ceiling instead of
            // deriving it from SafeMaxSide² (which clamped the 16:9 preset).
            MaxPixels = profile.MaxPixels,
        };
    }
}
