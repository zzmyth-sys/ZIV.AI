using System.Text.Json.Serialization;

namespace ZivAiEditor.Backend;

/// <summary>
/// Source-generated JSON context for <c>Template/loras.json</c> (mirrors
/// <c>PluginJsonContext</c> / <c>ModelProfileJsonContext</c>). Explicit <c>[JsonPropertyName]</c>
/// on every member wins over the policy; the snake_case policy is the convention fallback.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(LorasFileDto))]
internal partial class LoraJsonContext : JsonSerializerContext
{
}
