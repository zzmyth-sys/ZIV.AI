using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZivAiEditor.Backend;

/// <summary>
/// Local DTOs for <c>Template/models.json</c> (Step 8-2). JSON attributes stay in the Backend
/// (mechanism) and are mapped onto the kernel <c>ModelProfile</c> value object, keeping the
/// kernel free of serialization concerns. Unknown fields are ignored, so the Python-side
/// <c>sampler</c> block does not need to be modeled here.
/// </summary>
internal sealed class ModelsFileDto
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("models")]
    public List<ModelProfileDto>? Models { get; init; }
}

internal sealed class ModelProfileDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("native_side")]
    public int NativeSide { get; init; }

    [JsonPropertyName("safe_max_side")]
    public int SafeMaxSide { get; init; }

    [JsonPropertyName("min_side")]
    public int MinSide { get; init; }

    [JsonPropertyName("multiple_of")]
    public int MultipleOf { get; init; }

    [JsonPropertyName("max_pixels")]
    public int? MaxPixels { get; init; }

    [JsonPropertyName("tier_sides")]
    public Dictionary<string, int>? TierSides { get; init; }

    [JsonPropertyName("tier_labels")]
    public Dictionary<string, string>? TierLabels { get; init; }

    [JsonPropertyName("presets")]
    public List<AspectPresetDto>? Presets { get; init; }

    [JsonPropertyName("default")]
    public bool Default { get; init; }
}

internal sealed class AspectPresetDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ModelsFileDto))]
internal partial class ModelProfileJsonContext : JsonSerializerContext
{
}
