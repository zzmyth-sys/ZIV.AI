using System.Text.Json.Serialization;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Backend;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PingRequest))]
[JsonSerializable(typeof(SubmitRequest))]
[JsonSerializable(typeof(CancelRequest))]
[JsonSerializable(typeof(LoraOptions))]
[JsonSerializable(typeof(OptimizationOptions))]
[JsonSerializable(typeof(ResolutionPayload))]
internal partial class IpcJsonContext : JsonSerializerContext
{
}
