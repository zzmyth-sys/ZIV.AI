namespace ZivAiEditor.Contracts.Inference;

public interface IInferenceClient : IDisposable
{
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default);

    Task<InferenceTaskHandle> SubmitInpaintAsync(
        InpaintRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generalized submit entry point (Step 7). Expresses text-to-image,
    /// masked editing and outpainting through one <see cref="EditRequest"/>;
    /// <see cref="SubmitInpaintAsync"/> is the frozen, inpaint-only shim that
    /// delegates here. Implementations must honor the same serial/cancel/result
    /// semantics (Z18 / Z24).
    /// </summary>
    Task<InferenceTaskHandle> SubmitEditAsync(
        EditRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default);

    Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default);

    Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default);
}
