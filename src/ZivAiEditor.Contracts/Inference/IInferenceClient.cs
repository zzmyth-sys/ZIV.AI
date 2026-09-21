namespace ZivAiEditor.Contracts.Inference;

public interface IInferenceClient : IDisposable
{
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default);

    Task<InferenceTaskHandle> SubmitInpaintAsync(
        InpaintRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default);

    Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default);

    Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default);
}
