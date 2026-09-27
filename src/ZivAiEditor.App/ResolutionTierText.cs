using System;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Models;

namespace ZivAiEditor.App;

/// <summary>
/// Parses a CLI / picker resolution tier text into a <see cref="ResolutionPolicy"/> through the
/// shared model profiles (extracted to remove the verbatim duplicate between the headless quick
/// runner and the AI quick flow). Returns <c>null</c> for a missing / invalid / custom tier.
/// </summary>
internal static class ResolutionTierText
{
    public static ResolutionPolicy? Resolve(string? tier, ModelProfile profile)
    {
        if (string.IsNullOrWhiteSpace(tier)
            || !Enum.TryParse<ResolutionTier>(tier, ignoreCase: true, out var parsed)
            || parsed == ResolutionTier.Custom)
        {
            return null;
        }

        return ResolutionResolver.FromTier(parsed, profile);
    }
}
