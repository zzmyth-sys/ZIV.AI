namespace ZivAiEditor.Backend;

/// <summary>
/// Configuration for a single <see cref="LocalLlmClient"/> instance. Kept
/// deliberately small (ARCHITECTURE.md §11: no framework, no factory): later
/// scenarios (prompt rewriting, multi-image planning) reuse the same
/// <see cref="LocalLlmClient"/> type by constructing a different
/// <see cref="LlmClientOptions"/> (e.g. a higher <see cref="Temperature"/> for
/// creative prompt rewriting) rather than adding a new client class.
/// </summary>
public sealed class LlmClientOptions
{
    /// <summary>OpenAI-compatible chat completions endpoint (llama-server default).</summary>
    public string Endpoint { get; init; } = "http://127.0.0.1:8080/v1/chat/completions";

    /// <summary>Optional model alias; <c>null</c> lets the server pick its loaded model.</summary>
    public string? Model { get; init; }

    /// <summary>Per-call wall-clock budget.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Sampling temperature (planning wants stability, hence the low default).</summary>
    public double Temperature { get; init; } = 0.1;

    /// <summary>Upper bound on generated tokens.</summary>
    public int MaxTokens { get; init; } = 2048;

    /// <summary>
    /// Qwen3.5-family models must disable thinking or they exhaust
    /// <see cref="MaxTokens"/> without producing body text (see klein
    /// <c>llm_client.py</c>). When <c>false</c> the request carries
    /// <c>chat_template_kwargs.enable_thinking = false</c>.
    /// </summary>
    public bool EnableThinking { get; init; }
}