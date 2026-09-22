namespace ZivAiEditor.Contracts.Inference;

/// <summary>
/// Text-only LLM completion used by <c>LlmPlanner</c> to turn the user prompt
/// plus the available tool list into an <c>EditPlan</c> (see
/// <c>ARCHITECTURE.md</c> §5.3). Like <see cref="IInferenceClient"/> this is a
/// contract only: C# never loads a Python/LLM runtime directly (Z17); the
/// implementation is injected by the App layer (Z22: the planner can fall back
/// to a deterministic plan when this call fails or times out).
/// </summary>
public interface ILlmClient : IDisposable
{
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}