using System.Text.Json.Serialization;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Contracts.Execution;

/// <summary>One command entry from <c>Template/commands.json</c>.</summary>
public sealed class CommandDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("params")]
    public List<string> Params { get; init; } = new();

    [JsonPropertyName("tool")]
    public string Tool { get; init; } = "";

    [JsonPropertyName("template")]
    public string Template { get; init; } = "";

    /// <summary>
    /// Image-count dependent templates keyed by variant name ("single" / "multi"). Empty for
    /// commands that only carry a flat <see cref="Template"/>.
    /// </summary>
    [JsonPropertyName("variants")]
    public Dictionary<string, string> Variants { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Variant used when the image count is unknown (<c>imageCount &lt; 0</c>).</summary>
    [JsonPropertyName("defaultVariant")]
    public string DefaultVariant { get; init; } = "";

    /// <summary>Whether trailing arguments are folded into the last parameter (multi-word input).</summary>
    [JsonPropertyName("variadic")]
    public bool Variadic { get; init; }

    /// <summary>Text-to-image: the plan never carries a main image, regardless of the session.</summary>
    [JsonPropertyName("t2i")]
    public bool T2i { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>
    /// Optional LoRA applied to this command's edit (Step 8-1), carried verbatim from the data
    /// file. <see cref="LoraOptions.Path"/> may be a registry id (resolved by the Python backend
    /// against <c>Template/loras.json</c>) or a literal path. <c>null</c> = no LoRA.
    /// </summary>
    [JsonPropertyName("lora")]
    public LoraOptions? Lora { get; init; }
}
