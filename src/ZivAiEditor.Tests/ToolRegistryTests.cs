using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Tools;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 6 tool registry tests (no GPU).</summary>
public class ToolRegistryTests
{
    [Fact]
    public void Register_Get_All()
    {
        var registry = new ToolRegistry();
        var edit = new StubTool("QW21edit");
        var upscale = new StubTool("upscale");

        registry.Register(edit);
        registry.Register(upscale);

        Assert.Same(edit, registry.Get("QW21edit"));
        Assert.Same(upscale, registry.Get("upscale"));
        Assert.Null(registry.Get("missing"));
        Assert.Null(registry.Get("inpaint"));
        Assert.Null(registry.Get("img2img"));
        Assert.Equal(2, registry.All.Count);
        Assert.Contains(registry.All, tool => tool.Name == "QW21edit");
        Assert.Contains(registry.All, tool => tool.Name == "upscale");
    }

    [Fact]
    public void Unregister_Removes_Tool()
    {
        var registry = new ToolRegistry();
        registry.Register(new StubTool("QW21edit"));

        Assert.True(registry.Unregister("QW21edit"));
        Assert.Null(registry.Get("QW21edit"));
        Assert.Empty(registry.All);

        Assert.False(registry.Unregister("QW21edit"));
    }

    [Fact]
    public void Register_Same_Name_Replaces()
    {
        var registry = new ToolRegistry();
        var first = new StubTool("QW21edit");
        var second = new StubTool("QW21edit");

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.Get("QW21edit"));
        Assert.Single(registry.All);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var registry = new ToolRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    private sealed class StubTool : IEditTool
    {
        public StubTool(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public string Description => "stub";

        public IReadOnlyList<string> Capabilities => Array.Empty<string>();

        public bool CanHandle(EditStep step) => step.ToolName == Name;

        public Task<ToolResult> ExecuteAsync(
            ToolInput input,
            IProgress<StepProgress>? progress = null,
            CancellationToken ct = default)
            => Task.FromResult(new ToolResult { StepId = input.StepId, Success = true });
    }
}
