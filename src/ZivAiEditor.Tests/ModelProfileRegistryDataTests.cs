using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Models;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 8-2: <see cref="ModelProfileRegistry"/> loads <c>Template/models.json</c> (data-driven)
/// and falls back to the built-in Qwen-Image-2.1 profile when the file is missing / unreadable.
/// Adding a model is a data change, not a code change.
/// </summary>
public class ModelProfileRegistryDataTests
{
    private static string WriteModels(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "models.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Loads_Profiles_And_Default_From_Data_File()
    {
        var path = WriteModels("""
        {
          "version": "1",
          "models": [
            {
              "id": "alpha",
              "display_name": "Alpha",
              "native_side": 1024,
              "safe_max_side": 1024,
              "min_side": 256,
              "multiple_of": 8,
              "max_pixels": 1000000,
              "tier_sides": { "fast": 512, "balanced": 768, "high_quality": 1024 },
              "tier_labels": { "fast": "Alpha快速", "balanced": "Alpha均衡" },
              "presets": [ { "name": "1:1", "width": 1024, "height": 1024 } ]
            },
            {
              "id": "beta",
              "display_name": "Beta",
              "max_pixels": 2000000,
              "tier_sides": { "fast": 640 },
              "default": true
            }
          ]
        }
        """);

        var registry = new ModelProfileRegistry(path);

        Assert.Equal(2, registry.All.Count);
        Assert.Equal("beta", registry.Default.ModelId); // default: true wins
        Assert.Equal(2000000, registry.Default.MaxPixels);

        var alpha = registry.Get("alpha");
        Assert.NotNull(alpha);
        Assert.Equal(512, alpha!.TierSides[ResolutionTier.Fast]);
        Assert.Equal(768, alpha.TierSides[ResolutionTier.Balanced]);
        Assert.Equal(1024, alpha.TierSides[ResolutionTier.HighQuality]);
        Assert.Single(alpha.Presets);
        Assert.Equal("1:1", alpha.Presets[0].Name);
        // Step 8-3: tier_labels map to ModelProfile.TierLabels (only the labeled tiers appear).
        Assert.Equal("Alpha快速", alpha.TierLabels[ResolutionTier.Fast]);
        Assert.Equal("Alpha均衡", alpha.TierLabels[ResolutionTier.Balanced]);
        Assert.False(alpha.TierLabels.ContainsKey(ResolutionTier.HighQuality));
        Assert.Null(registry.Get("unknown"));
    }

    [Fact]
    public void Missing_File_Falls_Back_To_BuiltIn_Qwen()
    {
        var registry = new ModelProfileRegistry(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "models.json"));

        Assert.Equal(ModelProfileRegistry.QwenImage21Id, registry.Default.ModelId);
        Assert.Equal(7, registry.Default.Presets.Count);
    }

    [Fact]
    public void Malformed_File_Falls_Back_To_BuiltIn_Qwen()
    {
        var registry = new ModelProfileRegistry(WriteModels("{not json"));

        Assert.Equal(ModelProfileRegistry.QwenImage21Id, registry.Default.ModelId);
    }
}
