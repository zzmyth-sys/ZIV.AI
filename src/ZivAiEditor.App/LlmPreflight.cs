using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;

namespace ZivAiEditor.App;

/// <summary>Outcome of the <c>/生成</c> preflight (Step: prompt-rewriter flow).</summary>
public enum LlmPreflightStatus
{
    /// <summary>LLM reachable and the GPU has enough free VRAM to run the edit.</summary>
    Ready,

    /// <summary>The LLM <c>/health</c> probe failed; the rewriter service is not running.</summary>
    LlmUnreachable,

    /// <summary>The LLM answered but free VRAM is below the configured need.</summary>
    LowVram,
}

/// <summary>Result of <see cref="ILlmPreflight.CheckAsync"/>; <see cref="Message"/> is user-facing.</summary>
public sealed class LlmPreflightResult
{
    public LlmPreflightStatus Status { get; init; }

    public double VramUsedMb { get; init; }

    public double VramFreeMb { get; init; }

    public string Message { get; init; } = "";
}

/// <summary>Gates the <c>/生成</c> rewrite flow on LLM reachability and free VRAM.</summary>
public interface ILlmPreflight
{
    Task<LlmPreflightResult> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Probes the rewriter LLM's <c>/health</c> endpoint (a short, bounded GET) and, when it
/// answers, compares the NVML-reported device-wide VRAM usage against the configured need.
/// Never throws: every failure degrades to <see cref="LlmPreflightStatus.LlmUnreachable"/> so
/// the caller can show a hint and abort cleanly.
/// </summary>
public sealed class LlmPreflight : ILlmPreflight
{
    /// <summary>Bound for the <c>/health</c> probe; the LLM being down must fail fast.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.5);

    private readonly IInferenceClient _client;
    private readonly string _healthUrl;
    private readonly double _vramTotalMb;
    private readonly double _vramNeedMb;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>
    /// B17: whether the ComfyUI backend is already running. The VRAM probe runs only when it is,
    /// so <c>/生成</c> never starts the Python backend as a side effect of a "check".
    /// </summary>
    private readonly Func<bool> _isBackendRunning;

    public LlmPreflight(
        IInferenceClient client,
        string llmEndpoint,
        double vramTotalMb,
        double vramNeedMb,
        HttpClient? httpClient = null,
        Func<bool>? isBackendRunning = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _healthUrl = BuildHealthUrl(llmEndpoint);
        _vramTotalMb = vramTotalMb;
        _vramNeedMb = vramNeedMb;
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = httpClient is null;
        _isBackendRunning = isBackendRunning ?? DefaultBackendRunningProbe(client);
    }

    /// <summary>
    /// B17: default "is the backend already running?" probe. It reads the concrete
    /// <see cref="IpcInferenceClient"/>'s process state (the App layer already references Backend,
    /// so no contract change is needed); a non-IPC client (a test fake) is treated as running so
    /// the VRAM probe keeps its legacy behaviour.
    /// </summary>
    private static Func<bool> DefaultBackendRunningProbe(IInferenceClient client)
        => client is IpcInferenceClient ipc ? () => ipc.Process.IsProcessRunning : static () => true;

    public async Task<LlmPreflightResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(ProbeTimeout);
            using var response = await _http.GetAsync(_healthUrl, probeCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Unreachable();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[llm-preflight] probe failed: {ex.Message}");
            return Unreachable();
        }

        // B17: the backend is not running. Do NOT start it just to read VRAM — the normal submit
        // path starts it on demand (IpcInferenceClient.SubmitEditAsync). This is the same downgrade
        // as a VRAM read failure (D7): proceed Ready with the unknown-VRAM message.
        if (!_isBackendRunning())
        {
            return new LlmPreflightResult
            {
                Status = LlmPreflightStatus.Ready,
                Message = "就绪（显存状态未知）",
            };
        }

        try
        {
            var used = (await _client.CheckHealthAsync(ct).ConfigureAwait(false)).VramUsedMb;
            var free = _vramTotalMb - used;
            return free < _vramNeedMb
                ? new LlmPreflightResult
                {
                    Status = LlmPreflightStatus.LowVram,
                    VramUsedMb = used,
                    VramFreeMb = free,
                    Message = $"显存不足：已用 {used:F0}MB，可用 {free:F0}MB（需 {_vramNeedMb:F0}MB），仍可继续。",
                }
                : new LlmPreflightResult
                {
                    Status = LlmPreflightStatus.Ready,
                    VramUsedMb = used,
                    VramFreeMb = free,
                    Message = "就绪",
                };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The LLM probe already succeeded, so the rewriter is reachable; only the
            // ComfyUI backend's VRAM read failed. Do not hard-block on that (D7) — the
            // normal submit path surfaces any backend error. Treat as Ready (VRAM unknown).
            System.Diagnostics.Debug.WriteLine($"[llm-preflight] vram read failed: {ex.Message}");
            return new LlmPreflightResult
            {
                Status = LlmPreflightStatus.Ready,
                Message = "就绪（显存状态未知）",
            };
        }
    }

    /// <summary>Strips a trailing <c>/v1/chat/completions</c> and appends <c>/health</c>.</summary>
    private static string BuildHealthUrl(string llmEndpoint)
    {
        var endpoint = (llmEndpoint ?? "").Trim();
        const string suffix = "/v1/chat/completions";
        var baseUrl = endpoint.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? endpoint[..^suffix.Length]
            : endpoint.TrimEnd('/');
        return baseUrl.TrimEnd('/') + "/health";
    }

    private static LlmPreflightResult Unreachable() => new()
    {
        Status = LlmPreflightStatus.LlmUnreachable,
        Message = "请先启动 LLM 服务",
    };
}