using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using ZivAiEditor.Contracts.Models;

namespace ZivAiEditor.Backend;

/// <summary>
/// In-process <see cref="IModelProfileRegistry"/>. Step 8-2: the profiles are loaded from
/// <c>Template/models.json</c> (data) instead of being inlined (mechanism). Adding a model is a
/// data change; when the file is missing / unreadable the built-in Qwen-Image-2.1 profile is used
/// (Z28: no external dependency, never fails). Callers depend only on
/// <see cref="IModelProfileRegistry"/>, so they stay unchanged.
/// </summary>
public sealed class ModelProfileRegistry : IModelProfileRegistry
{
    /// <summary>Stable id for Qwen-Image-2.1 (matches <c>Template/models.json</c> / config.py).</summary>
    public const string QwenImage21Id = "qwen-image-2.1";

    private const string ModelFileName = "models.json";
    private const int DefaultMaxPixels = 4_700_000;

    private static readonly Dictionary<string, ResolutionTier> TierKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fast"] = ResolutionTier.Fast,
            ["balanced"] = ResolutionTier.Balanced,
            ["high_quality"] = ResolutionTier.HighQuality,
        };

    private readonly Dictionary<string, ModelProfile> _profiles;
    private readonly string _defaultId;

    /// <param name="modelsFilePath">
    /// Overrides the registry path (tests / App assembly use this). Defaults to
    /// <c>{AppContext.BaseDirectory}/Template/models.json</c>.
    /// </param>
    public ModelProfileRegistry(string? modelsFilePath = null)
    {
        var (profiles, defaultId) = Load(modelsFilePath);
        _profiles = profiles;
        _defaultId = defaultId;
    }

    public ModelProfile? Get(string modelId)
        => !string.IsNullOrEmpty(modelId) && _profiles.TryGetValue(modelId, out var profile)
            ? profile
            : null;

    public ModelProfile Default => _profiles[_defaultId];

    public IReadOnlyList<ModelProfile> All => _profiles.Values.ToArray();

    private static (Dictionary<string, ModelProfile> Profiles, string DefaultId) Load(string? modelsFilePath)
    {
        var profiles = new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase);
        var fallback = BuiltInDefault();
        var defaultId = fallback.ModelId;

        var path = modelsFilePath ?? Path.Combine(AppContext.BaseDirectory, "Template", ModelFileName);
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var dto = JsonSerializer.Deserialize(stream, ModelProfileJsonContext.Default.ModelsFileDto);
                foreach (var model in dto?.Models ?? new List<ModelProfileDto>())
                {
                    if (string.IsNullOrWhiteSpace(model.Id))
                    {
                        continue;
                    }

                    var profile = ToProfile(model);
                    profiles[profile.ModelId] = profile;
                    if (model.Default)
                    {
                        defaultId = profile.ModelId;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[model] models.json load failed: {ex.Message}");
            }
        }

        if (profiles.Count == 0)
        {
            profiles[fallback.ModelId] = fallback;
            defaultId = fallback.ModelId;
        }
        else if (!profiles.ContainsKey(defaultId))
        {
            defaultId = profiles.Keys.First();
        }

        return (profiles, defaultId);
    }

    private static ModelProfile ToProfile(ModelProfileDto dto)
    {
        var tiers = new Dictionary<ResolutionTier, int>();
        foreach (var pair in dto.TierSides ?? new Dictionary<string, int>())
        {
            if (TierKeys.TryGetValue(pair.Key, out var tier))
            {
                tiers[tier] = pair.Value;
            }
        }

        var labels = new Dictionary<ResolutionTier, string>();
        foreach (var pair in dto.TierLabels ?? new Dictionary<string, string>())
        {
            if (TierKeys.TryGetValue(pair.Key, out var tier) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                labels[tier] = pair.Value;
            }
        }

        var presets = (dto.Presets ?? new List<AspectPresetDto>())
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
            .Select(preset => new AspectPreset
            {
                Name = preset.Name!,
                Width = preset.Width,
                Height = preset.Height,
            })
            .ToArray();

        var id = dto.Id!.Trim();
        return new ModelProfile
        {
            ModelId = id,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? id : dto.DisplayName!,
            NativeSide = dto.NativeSide,
            SafeMaxSide = dto.SafeMaxSide,
            MaxPixels = dto.MaxPixels ?? DefaultMaxPixels,
            MinSide = dto.MinSide,
            MultipleOf = dto.MultipleOf,
            TierSides = tiers,
            TierLabels = labels,
            Presets = presets,
        };
    }

    /// <summary>The built-in Qwen-Image-2.1 profile, used when <c>models.json</c> is unavailable.</summary>
    private static ModelProfile BuiltInDefault() => new()
    {
        ModelId = QwenImage21Id,
        DisplayName = "Qwen-Image-2.1",
        NativeSide = 2048,
        SafeMaxSide = 2048,
        MaxPixels = DefaultMaxPixels,
        MinSide = 512,
        MultipleOf = 16,
        TierSides = new Dictionary<ResolutionTier, int>
        {
            [ResolutionTier.Fast] = 1024,
            [ResolutionTier.Balanced] = 1536,
            [ResolutionTier.HighQuality] = 2048,
        },
        TierLabels = new Dictionary<ResolutionTier, string>
        {
            [ResolutionTier.Fast] = "快速",
            [ResolutionTier.Balanced] = "均衡",
            [ResolutionTier.HighQuality] = "高质",
        },
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
}
