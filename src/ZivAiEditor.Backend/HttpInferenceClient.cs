using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Backend;

// Step 1 skeleton: no business logic. The real HTTP implementation arrives in Step 4.
internal sealed class HttpInferenceClient : IInferenceClient
{
    public Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<InferenceTaskHandle> SubmitInpaintAsync(
        InpaintRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
        => throw new NotImplementedException();

    public void Dispose()
    {
    }
}
