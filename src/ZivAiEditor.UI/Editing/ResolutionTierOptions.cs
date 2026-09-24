using System.Collections.Generic;
using System.Linq;
using ZivAiEditor.Contracts.Models;

namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Data-driven resolution-tier options for the UI tier picker (Step 8-3). Pure (no Avalonia),
/// so it is unit-testable. The tier <b>set</b> comes from <see cref="ModelProfile.TierSides"/>
/// (no side = the tier is not offered); the <b>labels</b> come from
/// <see cref="ModelProfile.TierLabels"/>. A missing label in production falls back to the enum
/// name on purpose — a misconfigured model file should be visible, not silently localized
/// (design-time, with no profile, uses a built-in Chinese fallback so the designer is never blank).
/// </summary>
public static class ResolutionTierOptions
{
    private const string CustomLabel = "自定义";

    /// <summary>Design-time fallback labels used only when no profile is attached.</summary>
    private static readonly IReadOnlyDictionary<ResolutionTier, string> FallbackLabels =
        new Dictionary<ResolutionTier, string>
        {
            [ResolutionTier.Fast] = "快速",
            [ResolutionTier.Balanced] = "均衡",
            [ResolutionTier.HighQuality] = "高质",
        };

    /// <summary>
    /// The offered tiers: the profile's <see cref="ModelProfile.TierSides"/> keys ascending,
    /// then <see cref="ResolutionTier.Custom"/>. With no profile, the built-in three + Custom.
    /// </summary>
    public static IReadOnlyList<ResolutionTier> Options(ModelProfile? profile)
    {
        var tiers = new List<ResolutionTier>();
        if (profile is null)
        {
            tiers.Add(ResolutionTier.Fast);
            tiers.Add(ResolutionTier.Balanced);
            tiers.Add(ResolutionTier.HighQuality);
        }
        else
        {
            tiers.AddRange(profile.TierSides.Keys.OrderBy(tier => (int)tier));
        }

        tiers.Add(ResolutionTier.Custom);
        return tiers;
    }

    /// <summary>Balanced when offered, else the first non-Custom tier, else Custom.</summary>
    public static ResolutionTier DefaultTier(ModelProfile? profile)
    {
        var options = Options(profile);
        if (options.Contains(ResolutionTier.Balanced))
        {
            return ResolutionTier.Balanced;
        }

        foreach (var tier in options)
        {
            if (tier != ResolutionTier.Custom)
            {
                return tier;
            }
        }

        return ResolutionTier.Custom;
    }

    /// <summary>
    /// The label for a tier: the profile's <c>tier_labels</c> when present; otherwise the enum
    /// name in production (visible config error); the built-in Chinese fallback only when no
    /// profile is attached (design-time). <see cref="ResolutionTier.Custom"/> is always "自定义".
    /// </summary>
    public static string Label(ModelProfile? profile, ResolutionTier tier)
    {
        if (tier == ResolutionTier.Custom)
        {
            return CustomLabel;
        }

        if (profile is not null)
        {
            return profile.TierLabels.TryGetValue(tier, out var label) && !string.IsNullOrWhiteSpace(label)
                ? label
                : tier.ToString();
        }

        return FallbackLabels.TryGetValue(tier, out var fallback) ? fallback : tier.ToString();
    }

    /// <summary>Label plus the tier's long edge (Custom has no side).</summary>
    public static string Display(ModelProfile? profile, ResolutionTier tier)
    {
        var label = Label(profile, tier);
        if (tier == ResolutionTier.Custom)
        {
            return label;
        }

        return profile is not null && profile.TierSides.TryGetValue(tier, out var side)
            ? $"{label} {side}"
            : label;
    }
}
