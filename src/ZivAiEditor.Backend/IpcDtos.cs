using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Backend;

internal sealed record PingRequest(string Type, string RequestId);

internal sealed record SubmitPayload(
    string? ImagePath,
    string? MaskPath,
    string Prompt,
    int Steps,
    long Seed,
    double Denoise,
    string? OutputPath,
    LoraOptions? Lora,
    OptimizationOptions? Optimizations,
    ResolutionPayload? Resolution,
    string? Anchor,
    IReadOnlyList<string> AdditionalImages);

/// <summary>Optional <c>submit.payload.resolution</c> (Step 6.5 / ipc_version 0.7).</summary>
internal sealed record ResolutionPayload(
    string? Mode,
    int? Side,
    int? Area,
    float? Scale,
    int? Width,
    int? Height,
    int? MaxPixels);

internal sealed record SubmitRequest(
    string Type,
    string RequestId,
    string TaskId,
    string Op,
    SubmitPayload Payload);

internal sealed record CancelRequest(string Type, string TaskId);
