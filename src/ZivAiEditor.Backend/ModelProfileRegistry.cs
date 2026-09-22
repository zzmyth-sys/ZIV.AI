using ZivAiEditor.Contracts.Models;

namespace ZivAiEditor.Backend;

/// <summary>
/// In-process <see cref="IModelProfileRegistry"/> seeded with the models ZIV.AI
/// ships. Step 6.5 registers Qwen-Image-2.1 only.
///
/// Extension seam (do not implement now): when more models or a Python-backed
/// capability source are needed, add profiles here, or introduce a new
/// <c>IModelProfileProvider</c> and swap the registry implementation — callers
/// depend on <see cref="IModelProfileRegistry"/> only, so they stay unchanged.
/// </summary>
public sealed class ModelProfileRegistry : IModelProfileRegistry
{
    /// <summary>Stable id for Qwen-Image-2.1 (matches <c>python/server/config.py</c> MODEL_NAME).</summary>
    public const string QwenImage21Id = "qwen-image-2.1";

    private readonly Dictionary<string, ModelProfile> _profiles;

    public ModelProfileRegistry()
    {
        var qwen = new ModelProfile
        {
            ModelId = QwenImage21Id,
            DisplayName = "Qwen-Image-2.1",
            NativeSide = 2048,
            SafeMaxSide = 2048,
            MinSide = 512,
            MultipleOf = 16,
            TierSides = new Dictionary<ResolutionTier, int>
            {
                [ResolutionTier.Fast] = 1024,
                [ResolutionTier.Balanced] = 1536,
                [ResolutionTier.HighQuality] = 2048,
            },
            // ~4.2M-pixel presets, multiples of 16. 1:1 and 16:9 are the values
            // fixed by the Step 6.5 decision; the rest follow the same area.
            Presets = new[]
            {
                new AspectPreset { Name = "1:1", Width = 2048, Height = 2048 },
                new AspectPreset { Name = "16:9", Width = 2752, Height = 1536 },
                new AspectPreset { Name = "9:16", Width = 1536, Height = 2752 },
                new AspectPreset { Name = "4:3", Width = 2368, Height = 1776 },
                new AspectPreset { Name = "3:4", Width = 1776, Height = 2368 },
                new AspectPreset { Name = "3:2", Width = 2496, Height = 1664 },
                new AspectPreset { Name = "2:3", Width = 1664, Height = 2496 },
            },
        };

        _profiles = new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [qwen.ModelId] = qwen,
        };
    }

    public ModelProfile? Get(string modelId)
        => !string.IsNullOrEmpty(modelId) && _profiles.TryGetValue(modelId, out var profile)
            ? profile
            : null;

    public ModelProfile Default => _profiles[QwenImage21Id];

    public IReadOnlyList<ModelProfile> All => _profiles.Values.ToArray();
}
