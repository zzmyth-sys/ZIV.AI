using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Agent;

/// <summary>
/// Turns a short user idea ("森林精灵") into a single vivid text-to-image prompt via an
/// injected <see cref="ILlmClient"/>. Used by the App's <c>/生成</c> confirmation flow; the
/// LLM is never loaded here (Z17), it is injected by the App layer.
/// </summary>
public interface IPromptExpander
{
    /// <summary>Rewrites <paramref name="description"/> into a ready-to-use T2I prompt.</summary>
    Task<string> ExpandAsync(string description, CancellationToken ct = default);
}

/// <summary>
/// Deterministic wrapper around <see cref="ILlmClient"/>: it sends one fixed system prompt and
/// the raw description, then trims the response. Failures propagate as the client's exception so
/// the App can surface them (Z22-style: the caller decides what to do).
/// </summary>
public sealed class PromptExpander : IPromptExpander
{
    /// <summary>Built-in system instruction; English, model-agnostic, no external file (Z28).</summary>
    public const string SystemPrompt =
        "You rewrite a short idea into ONE vivid text-to-image prompt for Qwen-Image-2.1. " +
        "Write as an observer describing the scene in flowing prose, not as a list. " +
        "Include 8-14 concrete spatial touchpoints across the foreground, midground and background, " +
        "and specify the direction and quality of the lighting. " +
        "Do not add headings, markdown, bullet points, quotes, or any meta commentary. " +
        "Respond with the prompt text only.";

    private readonly ILlmClient _llm;

    public PromptExpander(ILlmClient llm)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    public async Task<string> ExpandAsync(string description, CancellationToken ct = default)
    {
        var result = await _llm.CompleteAsync(SystemPrompt, description ?? "", ct).ConfigureAwait(false);
        return (result ?? "").Trim();
    }
}