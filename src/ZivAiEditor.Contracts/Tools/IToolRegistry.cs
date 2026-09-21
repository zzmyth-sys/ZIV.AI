namespace ZivAiEditor.Contracts.Tools;

public interface IToolRegistry
{
    void Register(IEditTool tool);
    bool Unregister(string toolName);
    IEditTool? Get(string toolName);
    IReadOnlyList<IEditTool> All { get; }
}
