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
    /// Backend path for this command (T2). Defaults to <see cref="CommandHandler.Edit"/> when a
    /// legacy file omits <c>handler</c>; routing is wired in T3. Serialized by name.
    /// </summary>
    [JsonPropertyName("handler")]
    public CommandHandler Handler { get; init; } = CommandHandler.Edit;

    /// <summary>
    /// Legacy single LoRA slot (Step 8-1; read-only compatibility from T2). Kept so older
    /// <c>commands.json</c> files still load; the template store writes <see cref="Loras"/>
    /// instead, and new code never populates this. <see cref="LoraOptions.Path"/> may be a
    /// registry id (resolved by the Python backend against <c>Template/loras.json</c>) or a
    /// literal path.
    /// </summary>
    [JsonPropertyName("lora")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LoraOptions? Lora { get; init; }

    /// <summary>
    /// Multi-slot LoRAs (T2). When non-empty it is the authoritative list; otherwise
    /// <see cref="Lora"/> is upgraded to a one-element list (see <see cref="EffectiveLoras"/>).
    /// Written by the template store; <c>null</c> = no LoRAs.
    /// </summary>
    [JsonPropertyName("loras")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LoraOptions>? Loras { get; init; }

    /// <summary>
    /// The effective LoRA list for consumers (T2): <see cref="Loras"/> when non-empty, else
    /// <see cref="Lora"/> upgraded to a one-element list, else empty. Not serialized.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<LoraOptions> EffectiveLoras => LoraSlots.Resolve(Loras, Lora);

    /// <summary>
    /// The effective handler for routing (T3.1): an explicit non-<see cref="CommandHandler.Edit"/>
    /// handler wins; otherwise a legacy <c>t2i=true</c> maps to <see cref="CommandHandler.T2I"/>
    /// (T2 field migration). Not serialized.
    /// </summary>
    [JsonIgnore]
    public CommandHandler EffectiveHandler
        => Handler != CommandHandler.Edit
            ? Handler
            : T2i
                ? CommandHandler.T2I
                : CommandHandler.Edit;
}
