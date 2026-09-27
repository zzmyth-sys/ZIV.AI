using ZivAiEditor.Diagnostics;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.Backend;

/// <summary>
/// Owns construction of the IPC <c>submit</c> payload from the generalized
/// <see cref="EditRequest"/> contract (Step 7 Phase 2). Kept free of the
/// client's <c>[SupportedOSPlatform]</c> attribute so tests can call it.
/// </summary>
internal static class IpcSubmitMapper
{
    public static SubmitRequest BuildSubmitRequest(string requestId, string taskId, EditRequest request)
    {
        if (DiagLog.IsEnabled)
        {
            // D7 diag (observation only): the actual C# submit payload image_path, to compare with D6.
            DiagLog.Log($"D7 submitPayload task={taskId} op={request.Op} image_path={request.ImagePath} mask={request.MaskPath}");
        }

        return new SubmitRequest(
            Type: "submit",
            RequestId: requestId,
            TaskId: taskId,
            Op: request.Op,
            Payload: new SubmitPayload(
                ImagePath: request.ImagePath,
                MaskPath: request.MaskPath,
                Prompt: request.Prompt,
                Steps: request.Steps,
                Seed: request.Seed,
                Denoise: request.Denoise,
                OutputPath: request.OutputPath,
                Lora: request.Lora,
                Loras: request.EffectiveLoras.Count > 0 ? request.EffectiveLoras.ToList() : null,
                Optimizations: request.Optimizations,
                Resolution: MapResolution(request.Resolution),
                Anchor: request.Anchor,
                AdditionalImages: request.AdditionalImages,
                ModelId: request.ModelId));
    }

    /// <summary>Maps the contract <see cref="ResolutionPolicy"/> onto the IPC payload (snake_case).</summary>
    private static ResolutionPayload? MapResolution(ResolutionPolicy? policy)
        => policy is null
            ? null
            : new ResolutionPayload(
                Mode: policy.Mode.ToString().ToLowerInvariant(),
                Side: policy.Side,
                Area: policy.Area,
                Scale: policy.Scale,
                Width: policy.Width,
                Height: policy.Height,
                MaxPixels: policy.MaxPixels);
}