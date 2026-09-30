using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZivAiEditor.Backend;

/// <summary>
/// Local DTOs for <c>Template/loras.json</c> (mirrors <c>ModelProfileFileDto</c> /
/// <c>PluginFileDto</c>). The JSON attributes stay in the Backend (mechanism); the App reads a
/// resolved <see cref="LoraEntryDto"/> to show a LoRA's real weight path / description, while the
/// Python backend keeps doing its own registry resolution at use-time. Unknown members (e.g. the
/// file's <c>_comment</c>) are ignored.
/// </summary>
internal sealed class LorasFileDto
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("loras")]
    public List<LoraEntryDto>? Loras { get; init; }
}

/// <summary>One entry in <c>Template/loras.json</c> (id + weight path + default strengths + owner + note).</summary>
public sealed class LoraEntryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// Ownership routing (2026-09-30): <c>model</c> (template-private) / <c>Plugin</c> (plugin-owned)
    /// / <c>none</c> (public manager). Absent = <c>none</c>.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    /// Origin absolute path copied into the unified directory (2026-09-30 source/path split).
    /// Only <c>source</c> ever points outside <c>&lt;comfy_root&gt;/models/loras</c>.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("default_strength_model")]
    public double? DefaultStrengthModel { get; init; }

    [JsonPropertyName("default_strength_clip")]
    public double? DefaultStrengthClip { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
