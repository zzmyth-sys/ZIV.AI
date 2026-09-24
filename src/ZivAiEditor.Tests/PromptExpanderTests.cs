using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Prompt-rewriter tests (fake LLM, no GPU).</summary>
public class PromptExpanderTests
{
    private sealed class FakeLlmClient : ILlmClient
    {
        private readonly string? _response;

        public FakeLlmClient(string? response) => _response = response;

        public string? LastSystemPrompt { get; private set; }

        public string? LastUserPrompt { get; private set; }

        public Task<string> CompleteAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken ct = default)
        {
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;
            return Task.FromResult(_response ?? "");
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task ExpandAsync_Passes_UserPrompt_And_Trims_Response()
    {
        var llm = new FakeLlmClient("  一只发光的森林精灵  ");
        var expander = new PromptExpander(llm);

        var result = await expander.ExpandAsync("森林精灵");

        Assert.Equal("森林精灵", llm.LastUserPrompt);
        Assert.Equal(PromptExpander.SystemPrompt, llm.LastSystemPrompt);
        Assert.Equal("一只发光的森林精灵", result);
    }

    [Fact]
    public async Task ExpandAsync_Null_Response_Returns_Empty()
    {
        var expander = new PromptExpander(new FakeLlmClient(null));

        Assert.Equal("", await expander.ExpandAsync("x"));
    }

    [Fact]
    public void SystemPrompt_Is_NonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(PromptExpander.SystemPrompt));
    }
}