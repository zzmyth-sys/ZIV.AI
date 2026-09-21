using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.Contracts.Tools;

public interface IEditTool
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<string> Capabilities { get; }
    bool CanHandle(EditStep step);

    Task<ToolResult> ExecuteAsync(
        ToolInput input,
        IProgress<StepProgress>? progress = null,
        CancellationToken ct = default);
}
