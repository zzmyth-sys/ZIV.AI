using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Models;

namespace ZivAiEditor.Backend;

/// <summary>
/// Translates a user-facing <see cref="ResolutionTier"/> into an absolute
/// <see cref="ResolutionPolicy"/> using a <see cref="ModelProfile"/> (the C#
/// layer owns the tier → number translation; Python is stateless — Z23).
///
/// Only the side-based tier mapping lives here. Upscale (Scale mode) and
/// outpaint (Explicit mode) build their own policies when those tools land in
/// Step 7 — no UpscaleResolver / OutpaintResolver yet (ARCHITECTURE.md §11).
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
            MaxPixels = profile.SafeMaxSide * profile.SafeMaxSide,
        };
    }
}
