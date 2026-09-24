using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZivAiEditor.Agent;

/// <summary>
/// Serialized re-run snapshot of one node (Step 9C.8-A), embedded in
/// <c>session.json</c> as the node's optional <c>rerun</c> object. Holds only what the
/// DAG cannot reconstruct: the UI resolution and the reference image names (relative
/// to the project directory, under <c>refs/</c>).
/// </summary>
internal sealed class SessionFileRerun
{
    [JsonPropertyName("resolution")]
    public SessionFileResolution? Resolution { get; init; }

    /// <summary>Relative reference-image names (<c>refs/{nodeId}_ref{n}{ext}</c>).</summary>
    [JsonPropertyName("additional_images")]
    public List<string> AdditionalImages { get; init; } = new();
}

/// <summary>
/// Serialized <c>ResolutionPolicy</c> for a re-run snapshot (Step 9C.8-A). The mode is
/// a readable string so the file stays self-describing; every numeric field is optional
/// and only the one matching <see cref="Mode"/> is meaningful.
/// </summary>
internal sealed class SessionFileResolution
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "Side";

    [JsonPropertyName("side")]
    public int? Side { get; init; }

    [JsonPropertyName("area")]
    public int? Area { get; init; }

    [JsonPropertyName("scale")]
    public float? Scale { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }

    [JsonPropertyName("max_pixels")]
    public int MaxPixels { get; init; } = 4_700_000;
}
