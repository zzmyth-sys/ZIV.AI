using ZivAiEditor.Contracts.Tools;

namespace ZivAiEditor.App;

/// <summary>
/// Temporary empty <see cref="IToolRegistry"/> used only to wire
/// <c>LlmPlanner</c> before the real registry lands in Step 7. With no tools
/// registered the planner advertises its built-in <c>inpaint</c> / <c>img2img</c>
/// defaults, which is the intended Step 5 behaviour.
/// TODO(Step 7): replace with <c>ZivAiEditor.Tools.ToolRegistry</c> once the
/// first concrete tools exist.
/// </summary>
internal sealed class EmptyToolRegistry : IToolRegistry
{
    public void Register(IEditTool tool)
    {
    }

    public bool Unregister(string toolName) => false;

    public IEditTool? Get(string toolName) => null;

    public IReadOnlyList<IEditTool> All => Array.Empty<IEditTool>();
}