using System.Text.Json.Serialization;

namespace ZivAiEditor.Contracts.Inference;

public sealed class OptimizationOptions
{
    // Batch 2A / D4: explicit wire names so the SnakeCaseLower policy does not emit
    // "mag_cache" (Python handlers.py reads "magcache"; contracts/ipc-protocol.md documents
    // "magcache" / "magcache_thresh").
    [JsonPropertyName("magcache")]
    public bool MagCache { get; init; }

    [JsonPropertyName("magcache_thresh")]
    public double MagCacheThresh { get; init; } = 0.24;
}
