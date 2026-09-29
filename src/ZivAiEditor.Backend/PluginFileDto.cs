using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZivAiEditor.Backend;

/// <summary>
/// Local DTOs for <c>Template/plugins.json</c> (batch 1). JSON attributes stay in the Backend
/// (mechanism) and are mapped onto <see cref="PluginDescriptor"/>; unknown fields are ignored,
/// so the Python-facing metadata does not need to be modeled here.
/// </summary>
internal sealed class PluginsFileDto
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("plugins")]
    public List<PluginDto>? Plugins { get; init; }
}

internal sealed class PluginDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Capability names the plugin exposes (must mirror the module's <c>PLUGIN_META</c>).</summary>
    [JsonPropertyName("capabilities")]
    public List<string>? Capabilities { get; init; }

    /// <summary>Seam names the plugin attaches to (S5). Mirrors the <c>seams</c> array in plugins.json.</summary>
    [JsonPropertyName("seams")]
    public List<string>? Seams { get; init; }

    [JsonPropertyName("dir")]
    public string? Dir { get; init; }

    [JsonPropertyName("entry")]
    public string? Entry { get; init; }

    [JsonPropertyName("deps")]
    public List<string>? Deps { get; init; }

    [JsonPropertyName("enabled_by_default")]
    public bool EnabledByDefault { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PluginsFileDto))]
internal partial class PluginJsonContext : JsonSerializerContext
{
}
