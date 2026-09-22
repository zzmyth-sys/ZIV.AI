using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Backend;

internal static class IpcFraming
{
    public const byte FrameJson = 0x01;
    public const byte FrameBinary = 0x02;
    public const int MaxFrameBytes = 64 * 1024 * 1024;

    private static readonly byte[] ShutdownFrame = Encoding.UTF8.GetBytes("{\"type\":\"shutdown\"}");

    public static async Task WriteFrameAsync(
        Stream stream,
        byte frameType,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), payload.Length + 1);
        header[4] = frameType;
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        }

        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static Task WriteJsonAsync(Stream stream, string json, CancellationToken ct)
        => WriteFrameAsync(stream, FrameJson, Encoding.UTF8.GetBytes(json), ct);

    public static async Task<(byte FrameType, byte[] Payload)?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Invalid IPC frame length: {length}.");
        }

        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Truncated IPC frame.");
        }

        var payload = new byte[length - 1];
        Array.Copy(body, 1, payload, 0, payload.Length);
        return (body[0], payload);
    }

    public static async Task TryWriteShutdownAsync(Stream stream)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await WriteFrameAsync(stream, FrameJson, ShutdownFrame, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0)
                {
                    return false;
                }

                throw new EndOfStreamException("Truncated IPC frame.");
            }

            offset += read;
        }

        return true;
    }
}

[SupportedOSPlatform("windows")]
public sealed class IpcInferenceClient : IInferenceClient
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

    public async Task<InferenceTaskHandle> SubmitInpaintAsync(
        InpaintRequest request,
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
            _tasks[taskId] = pending;
            lock (_stateLock)
            {
                _activeTaskId = taskId;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(Process.Options.ModelLoadTimeoutMs);
                var token = timeout.Token;

                var submit = new SubmitRequest(
                    Type: "submit",
                    RequestId: requestId,
                    TaskId: taskId,
                    Op: "inpaint",
                    Payload: new SubmitPayload(
                        ImagePath: request.ImagePath,
                        MaskPath: request.MaskPath,
                        Prompt: request.Prompt,
                        Steps: request.Steps,
                        Seed: request.Seed,
                        Denoise: request.Denoise,
                        OutputPath: request.OutputPath,
                        Lora: request.Lora,
                        Optimizations: request.Optimizations,
                        Resolution: MapResolution(request.Resolution)));

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
                    await TryForwardCancelAsync(stream, taskId, pending).ConfigureAwait(false);
                    throw new OperationCanceledException(ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"Inference did not complete within {Process.Options.ModelLoadTimeoutMs} ms.");
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
    /// fires during <see cref="SubmitInpaintAsync"/>. The cancel frame is sent
    /// with <see cref="CancellationToken.None"/> so the already-canceled caller
    /// token cannot block the write, then it waits briefly for the Python
    /// <c>canceled</c> acknowledgement.
    /// </summary>
    private async Task TryForwardCancelAsync(Stream stream, string taskId, PendingTask pending)
    {
        try
        {
            var json = JsonSerializer.Serialize(new CancelRequest("cancel", taskId), IpcJsonContext.Default.CancelRequest);
            await WriteJsonAsync(stream, json, CancellationToken.None).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(Process.Options.RequestTimeoutMs);
            await pending.CancelSignal.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort only: the submit path is already unwinding.
        }
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

    private void EnsureReceiveStarted(Stream stream)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_receiveStream, stream) && _receiveLoop is { IsCompleted: false })
            {
                return;
            }

            _receiveStream = stream;
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(stream, _lifetime.Token));
            _lastHeartbeatAt = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref _heartbeatLostRaised, 0);

            if (_heartbeatLoop is null or { IsCompleted: true })
            {
                _heartbeatLoop = Task.Run(() => HeartbeatWatchAsync(_lifetime.Token));
            }
        }
    }

    private async Task ReceiveLoopAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            await ReceiveLoopCoreAsync(stream, ct).ConfigureAwait(false);
        }
        finally
        {
            bool current;
            lock (_stateLock)
            {
                current = ReferenceEquals(_receiveStream, stream);
                if (current)
                {
                    _receiveStream = null;
                    _receiveLoop = null;
                }
            }

            if (current)
            {
                FailInFlight(BackendRestarted());
            }
        }
    }

    private void FailInFlight(Exception error)
    {
        foreach (var pair in _tasks)
        {
            pair.Value.Completion.TrySetException(error);
        }

        foreach (var pair in _pings)
        {
            pair.Value.TrySetException(error);
        }
    }

    private async Task ReceiveLoopCoreAsync(Stream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            (byte FrameType, byte[] Payload)? frame;
            try
            {
                frame = await IpcFraming.ReadFrameAsync(stream, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                break;
            }

            if (frame is null)
            {
                break;
            }

            try
            {
                DispatchFrame(frame.Value);
            }
            catch (JsonException)
            {
                // Ignore malformed control frames; the stream stays usable.
            }
        }
    }

    private async Task HeartbeatWatchAsync(CancellationToken ct)
    {
        var thresholdMs = Math.Max(250, Process.Options.HeartbeatLostAfterMs);
        var pollMs = Math.Max(200, thresholdMs / 4);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(pollMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            DateTimeOffset? last;
            lock (_stateLock)
            {
                last = _lastHeartbeatAt;
            }

            if (last is null)
            {
                continue;
            }

            var lost = (DateTimeOffset.UtcNow - last.Value).TotalMilliseconds > thresholdMs;
            if (lost)
            {
                if (Interlocked.Exchange(ref _heartbeatLostRaised, 1) == 0)
                {
                    HeartbeatLost?.Invoke();
                }
            }
            else
            {
                Interlocked.Exchange(ref _heartbeatLostRaised, 0);
            }
        }
    }

    private void DispatchFrame((byte FrameType, byte[] Payload) frame)
    {
        if (frame.FrameType == IpcFraming.FrameBinary)
        {
            HandleBinaryFrame(frame.Payload);
            return;
        }

        if (frame.FrameType != IpcFraming.FrameJson)
        {
            return;
        }

        using var document = JsonDocument.Parse(frame.Payload);
        var root = document.RootElement;
        var messageType = ReadString(root, "type");

        if (messageType == "heartbeat")
        {
            lock (_stateLock)
            {
                _lastHeartbeatAt = DateTimeOffset.UtcNow;
            }

            Interlocked.Exchange(ref _heartbeatLostRaised, 0);
            HeartbeatReceived?.Invoke();
            return;
        }

        if (messageType == "pong")
        {
            var pingId = ReadString(root, "request_id");
            if (pingId is not null && _pings.TryRemove(pingId, out var pingCompletion))
            {
                pingCompletion.TrySetResult(MapHealth(root));
            }

            return;
        }

        var taskId = ReadString(root, "task_id");
        if (taskId is null || !_tasks.TryGetValue(taskId, out var task))
        {
            if (messageType == "error")
            {
                var pingId = ReadString(root, "request_id");
                if (pingId is not null && _pings.TryRemove(pingId, out var pingCompletion))
                {
                    pingCompletion.TrySetException(
                        new InvalidOperationException(ReadString(root, "message") ?? "Inference backend returned an error."));
                }
            }

            return;
        }

        switch (messageType)
        {
            case "accepted":
                break;

            case "progress":
                var detail = MapProgress(root);
                ProgressReceived?.Invoke(detail);
                task.Progress?.Report(new InferenceProgress
                {
                    Fraction = detail.Fraction,
                    Message = detail.Message,
                });

                break;

            case "preview":
                task.PreviewStep = (int)ReadDouble(root, "step");
                task.PreviewTotal = (int)ReadDouble(root, "total");
                break;

            case "result":
                var result = MapResult(root);
                ResultReceived?.Invoke(result);
                task.Completion.TrySetResult(new InferenceTaskHandle
                {
                    TaskId = taskId,
                    Status = TaskStatus.Succeeded,
                });

                break;

            case "canceled":
                task.CancelSignal.TrySetResult(true);
                task.Completion.TrySetResult(new InferenceTaskHandle
                {
                    TaskId = taskId,
                    Status = TaskStatus.Canceled,
                });

                break;

            case "error":
                task.Completion.TrySetException(new InferenceBackendException(
                    ReadString(root, "code") ?? "backend_error",
                    ReadString(root, "message") ?? "Inference backend returned an error."));
                break;
        }
    }

    private async Task WriteJsonAsync(Stream stream, string json, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await IpcFraming.WriteJsonAsync(stream, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void HandleBinaryFrame(byte[] payload)
    {
        if (!TryParsePreviewFrame(payload, out var taskId, out var jpeg))
        {
            return;
        }

        var step = 0;
        var total = 0;
        if (taskId is not null && _tasks.TryGetValue(taskId, out var task))
        {
            step = task.PreviewStep;
            total = task.PreviewTotal;
        }

        PreviewReceived?.Invoke(new PreviewFrame
        {
            TaskId = taskId,
            Step = step,
            Total = total,
            JpegBytes = jpeg,
        });
    }

    private static InferenceProgressDetail MapProgress(JsonElement root)
        => new()
        {
            TaskId = ReadString(root, "task_id"),
            Stage = ReadString(root, "stage"),
            SubStage = ReadString(root, "sub_stage"),
            Step = (int)ReadDouble(root, "step"),
            Total = (int)ReadDouble(root, "total"),
            Fraction = ReadDouble(root, "fraction"),
            Message = ReadString(root, "message"),
        };

    private void HandleBinaryFrame(byte[] payload, string taskId, int step, int total)
    {
        if (!TryParsePreviewFrame(payload, out var frameTaskId, out var jpeg))
        {
            return;
        }

        if (frameTaskId is not null && frameTaskId != taskId)
        {
            return;
        }

        PreviewReceived?.Invoke(new PreviewFrame
        {
            TaskId = frameTaskId,
            Step = step,
            Total = total,
            JpegBytes = jpeg,
        });
    }

    private static bool TryParsePreviewFrame(byte[] payload, out string? taskId, out byte[] jpeg)
    {
        taskId = null;
        jpeg = Array.Empty<byte>();

        // [4B task_id length][task_id][4B JPEG length][JPEG], little endian.
        if (payload.Length < 8)
        {
            return false;
        }

        var idLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
        var jpegLengthOffset = 4 + idLength;
        if (idLength < 0 || jpegLengthOffset + 4 > payload.Length)
        {
            return false;
        }

        var jpegLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(jpegLengthOffset, 4));
        var jpegStart = jpegLengthOffset + 4;
        if (jpegLength < 0 || jpegStart + jpegLength > payload.Length)
        {
            return false;
        }

        taskId = Encoding.UTF8.GetString(payload, 4, idLength);
        jpeg = payload[jpegStart..(jpegStart + jpegLength)];
        return true;
    }

    private static InferenceResultDetail MapResult(JsonElement root)
        => new()
        {
            TaskId = ReadString(root, "task_id"),
            OutputPath = ReadString(root, "output_path"),
            DurationMs = ReadDouble(root, "duration_ms"),
            Width = (int)ReadDouble(root, "width"),
            Height = (int)ReadDouble(root, "height"),
            Seed = (long)ReadDouble(root, "seed"),
        };

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

    private static HealthStatus MapHealth(JsonElement root)
    {
        var models = new List<ModelStatus>();
        if (root.TryGetProperty("models", out var modelsProperty) && modelsProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in modelsProperty.EnumerateArray())
            {
                models.Add(new ModelStatus
                {
                    Name = ReadString(item, "name") ?? string.Empty,
                    Loaded = ReadBool(item, "loaded"),
                    LastUsedAt = ReadTimestamp(item, "last_used_at"),
                });
            }
        }

        return new HealthStatus
        {
            Status = ReadString(root, "status") ?? "down",
            Version = ReadString(root, "version"),
            ModelStatus = ReadString(root, "model_status") ?? "not_loaded",
            VramUsedMb = ReadDouble(root, "vram_used_mb"),
            IdleUnloadSeconds = (int)ReadDouble(root, "idle_unload_seconds"),
            Models = models,
        };
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.True;

    private static double ReadDouble(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0d;

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
           && value.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Routing state for one in-flight <c>submit</c>; completed by the receive
    /// loop when a <c>result</c> / <c>canceled</c> / <c>error</c> frame arrives.
    /// </summary>
    private sealed class PendingTask
    {
        public PendingTask(string taskId, IProgress<InferenceProgress>? progress)
        {
            TaskId = taskId;
            Progress = progress;
        }

        public string TaskId { get; }

        public IProgress<InferenceProgress>? Progress { get; }

        public TaskCompletionSource<InferenceTaskHandle> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancelSignal { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int PreviewStep { get; set; }

        public int PreviewTotal { get; set; }
    }
}

internal sealed record PingRequest(string Type, string RequestId);

internal sealed record SubmitPayload(
    string? ImagePath,
    string? MaskPath,
    string Prompt,
    int Steps,
    long Seed,
    double Denoise,
    string? OutputPath,
    LoraOptions? Lora,
    OptimizationOptions? Optimizations,
    ResolutionPayload? Resolution);

/// <summary>Optional <c>submit.payload.resolution</c> (Step 6.5 / ipc_version 0.6).</summary>
internal sealed record ResolutionPayload(
    string? Mode,
    int? Side,
    int? Area,
    float? Scale,
    int? Width,
    int? Height,
    int? MaxPixels);

internal sealed record SubmitRequest(
    string Type,
    string RequestId,
    string TaskId,
    string Op,
    SubmitPayload Payload);

internal sealed record CancelRequest(string Type, string TaskId);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PingRequest))]
[JsonSerializable(typeof(SubmitRequest))]
[JsonSerializable(typeof(CancelRequest))]
[JsonSerializable(typeof(LoraOptions))]
[JsonSerializable(typeof(OptimizationOptions))]
[JsonSerializable(typeof(ResolutionPayload))]
internal partial class IpcJsonContext : JsonSerializerContext
{
}
