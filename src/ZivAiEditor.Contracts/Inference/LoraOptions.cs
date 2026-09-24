using System.Text.Json.Serialization;

namespace ZivAiEditor.Contracts.Inference;

/// <summary>
/// One LoRA applied to a generation / edit (Step 4 seam; wired data-driven in Step 8-1).
/// <see cref="Path"/> is either a registry id from <c>Template/loras.json</c> (resolved by the
/// Python backend) or a literal weight path. Serialized snake_case on the IPC payload and in
/// <c>Template/commands.json</c> (<c>path</c> / <c>strength_model</c> / <c>strength_clip</c>).
///
/// <para><b>Step 8-2 (authorized revision)</b>: the strengths are <see cref="double"/>? so an
/// omitted value (<c>null</c>) is distinguishable from an explicit <c>0</c>. The source-generated
/// deserializer leaves a missing nullable at <c>null</c> (previously it filled the non-nullable
/// default <c>0.0</c>, which had to be normalized back to <c>1.0</c> and, as a side effect,
/// rewrote an explicit <c>0</c> to <c>1.0</c> — a semantic inversion). <c>null</c> = unset
/// (callers apply 1.0 / the registry default); <c>0</c> = fully suppress that side.</para>
/// </summary>
public sealed class LoraOptions
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    /// <summary>Model-side strength; <c>null</c> = unset (default 1.0), <c>0</c> = suppress.</summary>
    [JsonPropertyName("strength_model")]
    public double? StrengthModel { get; init; }

    /// <summary>CLIP-side strength; <c>null</c> = unset (default 1.0), <c>0</c> = suppress.</summary>
    [JsonPropertyName("strength_clip")]
    public double? StrengthClip { get; init; }
}
