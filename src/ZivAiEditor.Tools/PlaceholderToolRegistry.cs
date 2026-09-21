using ZivAiEditor.Contracts.Tools;

namespace ZivAiEditor.Tools;

// Step 1 skeleton: no business logic. ToolRegistry / concrete tools arrive in later steps.
internal sealed class PlaceholderToolRegistry : IToolRegistry
{
    public void Register(IEditTool tool) => throw new NotImplementedException();

    public bool Unregister(string toolName) => throw new NotImplementedException();

    public IEditTool? Get(string toolName) => throw new NotImplementedException();

    public IReadOnlyList<IEditTool> All => throw new NotImplementedException();
}
