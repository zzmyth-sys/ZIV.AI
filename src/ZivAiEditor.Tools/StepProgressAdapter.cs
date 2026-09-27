using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Contracts.Tools;

namespace ZivAiEditor.Tools;

/// <summary>
/// Forwards <see cref="InferenceProgress"/> as <see cref="StepProgress"/> synchronously
/// (shared by the Qwen edit tools; extracted to remove the verbatim duplicate).
/// </summary>
internal sealed class StepProgressAdapter : IProgress<InferenceProgress>
{
    private readonly IProgress<StepProgress> _inner;
    private readonly string _stepId;

    public StepProgressAdapter(IProgress<StepProgress> inner, string stepId)
    {
        _inner = inner;
        _stepId = stepId;
    }

    public void Report(InferenceProgress value)
        => _inner.Report(new StepProgress
        {
            StepId = _stepId,
            Fraction = value.Fraction,
            Message = value.Message,
        });
}
