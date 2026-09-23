namespace ZivAiEditor.Backend;

/// <summary>
/// Detailed view of a Python <c>progress</c> IPC frame (see
/// <c>contracts/ipc-protocol.md</c> §3.2). The contract-level
/// <see cref="ZivAiEditor.Contracts.Inference.InferenceProgress"/> stays frozen
/// and only carries <c>Fraction</c> / <c>Message</c>; the additive
/// <c>stage</c> / <c>sub_stage</c> fields introduced in Step 2.2 are surfaced
/// here so callers (and tests) can observe the lazy-load lifecycle without
/// touching the frozen contract.
///
/// <para><b>Boundary (Step 9C.3-R #9)</b>: this type is for <b>Backend
/// diagnostics / event enrichment only</b> — it never crosses the frozen contract
/// boundary. The boundary carries <see cref="ZivAiEditor.Contracts.Inference.InferenceProgress"/>
/// (<c>Fraction</c> / <c>Message</c>) instead. The two are two projections of the
/// same frame: this detail is the richer one, <c>InferenceProgress</c> the minimal
/// frozen one.</para>
/// </summary>
public sealed class InferenceProgressDetail
{
    public string? TaskId { get; init; }
    public string? Stage { get; init; }
    public string? SubStage { get; init; }
    public int Step { get; init; }
    public int Total { get; init; }
    public double Fraction { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// A preview decoded from a <c>0x02</c> IPC binary frame (see
/// <c>contracts/ipc-protocol.md</c> §3.5). <see cref="Step"/> / <see cref="Total"/>
/// come from the preceding <c>preview</c> control frame.
/// </summary>
public sealed class PreviewFrame
{
    public string? TaskId { get; init; }
    public int Step { get; init; }
    public int Total { get; init; }
    public byte[] JpegBytes { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// Terminal success info from a Python <c>result</c> IPC frame (Step 2.3).
/// </summary>
public sealed class InferenceResultDetail
{
    public string? TaskId { get; init; }
    public string? OutputPath { get; init; }

    /// <summary>
    /// Backend-reported duration of the sampling + VAE-decode work only (Python
    /// <c>result.duration_ms</c>). It excludes IPC submission, lazy model load and
    /// queue wait — see <c>ToolResult.Duration</c> (one IPC submit) and the UI's
    /// end-to-end stopwatch (Step 9C.3-R #2).
    /// </summary>
    public double DurationMs { get; init; }

    /// <summary>
    /// Actual width of the image the backend produced. This is the <b>output</b> size
    /// (after any resize / fallback), not the requested target
    /// (<c>ResolutionPolicy.Width</c>) nor a preset (<c>AspectPreset.Width</c>).
    /// </summary>
    public int Width { get; init; }

    /// <summary>Actual output height produced by the backend (not the request target).</summary>
    public int Height { get; init; }

    public long Seed { get; init; }
}

/// <summary>
/// Raised when the backend answers a <c>submit</c> with an <c>error</c> frame.
/// </summary>
public sealed class InferenceBackendException : Exception
{
    public InferenceBackendException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
