using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text.Json;
using ZivAiEditor.Contracts.Inference;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Backend;

[SupportedOSPlatform("windows")]
public sealed partial class IpcInferenceClient : IInferenceClient
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _submitGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, PendingTask> _tasks = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<HealthStatus>> _pings = new();
    private readonly bool _ownsProcess;

    private Stream? _receiveStream;
    private Task? _receiveLoop;
    private Task? _heartbeatLoop;
    private int _heartbeatLostRaised;
    private string? _activeTaskId;
    private DateTimeOffset? _lastHeartbeatAt;
    private bool _disposed;

    public IpcInferenceClient(PythonProcessManager process, bool ownsProcess = false)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));
        _ownsProcess = ownsProcess;
        Process.AttachInferenceClient(this);
        EnsureWatchdogStarted();
    }

    public PythonProcessManager Process { get; }

    /// <summary>
    /// Raised for every <c>progress</c> frame received from the backend, with
    /// the additive <c>stage</c> / <c>sub_stage</c> fields (Step 2.2).
    /// </summary>
    public event Action<InferenceProgressDetail>? ProgressReceived;

    /// <summary>
    /// Raised for every <c>preview</c> binary frame (<c>0x02</c>, Step 2.3) with
    /// the decoded JPEG bytes.
    /// </summary>
    public event Action<PreviewFrame>? PreviewReceived;

    /// <summary>
    /// Raised once a task finishes successfully and the backend reports its
    /// new output file (Z24).
    /// </summary>
    public event Action<InferenceResultDetail>? ResultReceived;

    /// <summary>
    /// Raised for every backend <c>heartbeat</c> frame (Step 3).
    /// </summary>
    public event Action? HeartbeatReceived;

    /// <summary>
    /// Raised once when heartbeats stop for longer than
    /// <see cref="PythonBackendOptions.HeartbeatLostAfterMs"/>. The App decides
    /// whether to restart the backend (Step 4).
    /// </summary>
    public event Action? HeartbeatLost;

    /// <summary>
    /// Timestamp of the most recent received heartbeat, or <see langword="null"/>
    /// if none has arrived yet.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAt
    {
        get
        {
            lock (_stateLock)
            {
                return _lastHeartbeatAt;
            }
        }
    }

    /// <summary>
    /// The task currently being processed by <see cref="SubmitInpaintAsync"/>,
    /// or <see langword="null"/> when the client is idle. Cancellation is
    /// dispatched against this task.
    /// </summary>
    public string? ActiveTaskId
    {
        get
        {
            lock (_stateLock)
            {
                return _activeTaskId;
            }
        }
    }

    public async Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var stream = await Process.EnsureStartedAsync(ct).ConfigureAwait(false);
        EnsureReceiveStarted(stream);
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<HealthStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pings[requestId] = completion;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Process.Options.RequestTimeoutMs);
            var token = timeout.Token;

            var request = JsonSerializer.Serialize(new PingRequest("ping", requestId), IpcJsonContext.Default.PingRequest);
            await WriteJsonAsync(stream, request, token).ConfigureAwait(false);

            try
            {
                return await completion.Task.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"No IPC response within {Process.Options.RequestTimeoutMs} ms.");
            }
        }
        finally
        {
            _pings.TryRemove(requestId, out _);
        }
    }

    public Task<InferenceTaskHandle> SubmitInpaintAsync(
        InpaintRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var edit = new EditRequest
        {
            Op = EditOps.Inpaint,
            ImagePath = request.ImagePath,
            MaskPath = request.MaskPath,
            Prompt = request.Prompt,
            Steps = request.Steps,
            Seed = request.Seed,
            Denoise = request.Denoise,
            OutputPath = request.OutputPath,
            Resolution = request.Resolution,
            Lora = request.Lora,
            Optimizations = request.Optimizations,
            Anchor = null,
            // InpaintRequest has no model id (Step 8-2); the legacy path uses the default model.
            ModelId = null,
        };

        return SubmitEditAsync(edit, progress, ct);
    }

    /// <summary>
    /// Generalized single-inference submit (Step 7). The <see cref="EditRequest.Op"/>
    /// selects text-to-image / inpaint / outpaint; everything else is shared.
    /// </summary>
    public async Task<InferenceTaskHandle> SubmitEditAsync(
        EditRequest request,
        IProgress<InferenceProgress>? progress = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        // Z18: only one task may run at a time.
        await _submitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stream = await Process.EnsureStartedAsync(ct).ConfigureAwait(false);
            EnsureReceiveStarted(stream);

            var requestId = Guid.NewGuid().ToString("N");
            var taskId = Guid.NewGuid().ToString("N");
            var pending = new PendingTask(taskId, progress);
            pending.AcceptedAt = DateTimeOffset.UtcNow;
            pending.LastProgressAt = pending.AcceptedAt;
            _tasks[taskId] = pending;
            lock (_stateLock)
            {
                _activeTaskId = taskId;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(Process.Options.ModelLoadTimeoutMs);
                // L4: the receipt loop disables this source on the first sampling frame,
                // so the 180 s load budget cannot cap the (unbounded) sampling phase.
                // A genuinely stuck sampling task is handled by the L1 watchdog instead.
                pending.Timeout = timeout;
                var token = timeout.Token;

                var submit = IpcSubmitMapper.BuildSubmitRequest(requestId, taskId, request);
                var json = JsonSerializer.Serialize(submit, IpcJsonContext.Default.SubmitRequest);
                try
                {
                    await WriteJsonAsync(stream, json, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    throw BackendRestarted();
                }

                try
                {
                    var handle = await pending.Completion.Task.WaitAsync(token).ConfigureAwait(false);
                    if (handle.Status == TaskStatus.Succeeded)
                    {
                        Process.NotifyTaskSucceeded();
                    }

                    return handle;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // The caller canceled: forward the cancel to the Python
                    // backend so it interrupts sampling instead of running to
                    // completion (Z18/Z20). Best effort — the task is already
                    // being torn down.
                    await TryForwardCancelAsync(stream, taskId, pending, Process.Options.RequestTimeoutMs).ConfigureAwait(false);
                    throw new OperationCanceledException(ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // C2: a timeout must actively stop the backend, not just drop
                    // the eventual result. Forward a cancel; only when the frame
                    // could not be sent (dead pipe) do we ask for a restart. The
                    // restart respects AutoRestartEnabled / the attempt budget, so
                    // a disabled or exhausted backend is left as-is on purpose.
                    var (sent, _) = await TryForwardCancelAsync(stream, taskId, pending, Process.Options.RequestTimeoutMs).ConfigureAwait(false);
                    if (!sent)
                    {
                        _ = Process.RequestRestartAsync();
                    }

                    throw new TimeoutException(
                        $"Inference did not start sampling within {Process.Options.ModelLoadTimeoutMs} ms (L4: the sampling phase itself is unbounded).");
                }
            }
            finally
            {
                _tasks.TryRemove(taskId, out _);
                lock (_stateLock)
                {
                    if (string.Equals(_activeTaskId, taskId, StringComparison.Ordinal))
                    {
                        _activeTaskId = null;
                    }
                }

                pending.CancelSignal.TrySetResult(false);
            }
        }
        finally
        {
            _submitGate.Release();
        }
    }

    public Task<InferenceTask> GetTaskAsync(string taskId, CancellationToken ct = default)
        => throw new NotSupportedException("GetTaskAsync is scheduled for a later step.");

    public async Task<bool> CancelTaskAsync(string taskId, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(taskId);

        if (!_tasks.TryGetValue(taskId, out var pending))
        {
            return false;
        }

        var stream = await Process.EnsureStartedAsync(ct).ConfigureAwait(false);

        var json = JsonSerializer.Serialize(new CancelRequest("cancel", taskId), IpcJsonContext.Default.CancelRequest);
        await WriteJsonAsync(stream, json, ct).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Process.Options.RequestTimeoutMs);
        try
        {
            return await pending.CancelSignal.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Best-effort cancel used when the caller's <see cref="CancellationToken"/>
    /// fires, the submit times out, or the L1 watchdog decides the task is stuck.
    /// The cancel frame is written under a bounded (independent of the caller)
    /// token so a stalled / non-reading backend cannot block recovery forever.
    /// Returns <c>(Sent, Acked)</c>: <c>Sent</c> is false only when the write
    /// failed / timed out (dead or stuck pipe); <c>Acked</c> is true only when
    /// Python's <c>canceled</c> frame arrived within <paramref name="ackTimeoutMs"/>.
    /// The restart decision uses these (a slow / missing ack means a restart).
    /// </summary>
    private async Task<(bool Sent, bool Acked)> TryForwardCancelAsync(
        Stream stream,
        string taskId,
        PendingTask pending,
        int ackTimeoutMs)
    {
        try
        {
            using var writeTimeout = new CancellationTokenSource(Math.Max(250, ackTimeoutMs));
            var json = JsonSerializer.Serialize(new CancelRequest("cancel", taskId), IpcJsonContext.Default.CancelRequest);
            await WriteJsonAsync(stream, json, writeTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The pipe is gone / stuck: report it so the caller can restart the backend.
            return (false, false);
        }

        var acked = false;
        try
        {
            using var timeout = new CancellationTokenSource(ackTimeoutMs);
            await pending.CancelSignal.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            acked = true;
        }
        catch (Exception)
        {
            // Best effort: no ack within the window.
        }

        return (true, acked);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Process.DetachInferenceClient(this);
        _lifetime.Cancel();
        _lifetime.Dispose();
        _writeGate.Dispose();
        _submitGate.Dispose();
        if (_ownsProcess)
        {
            Process.Dispose();
        }
    }

    private static InferenceBackendException BackendRestarted()
        => new(
            "BACKEND_RESTARTED",
            "The inference backend connection was lost; the backend is being restarted.");
}
