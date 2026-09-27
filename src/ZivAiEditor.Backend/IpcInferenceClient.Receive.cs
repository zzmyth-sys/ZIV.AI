using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ZivAiEditor.Contracts.Inference;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Backend;

public sealed partial class IpcInferenceClient
{
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
                var vram = ReadDouble(root, "vram_used_mb");
                if (vram > 0)
                {
                    _lastVramMb = vram;
                }
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
                    return;
                }

                // B8: an error frame without a task_id used to be dropped silently, so the
                // submit then waited out its full budget (up to ModelLoadTimeoutMs) before
                // failing. Z18 is single-slot, so attribute it to the one in-flight task.
                var targetTaskId = ResolveErrorTargetTaskId(taskId, ActiveTaskId);
                if (targetTaskId is not null && _tasks.TryGetValue(targetTaskId, out var errorTask))
                {
                    var code = ReadString(root, "code") ?? "backend_error";
                    var message = ReadString(root, "message") ?? "Inference backend returned an error.";
                    errorTask.Completion.TrySetException(new InferenceBackendException(code, message));
                    return;
                }

                Debug.WriteLine($"[ipc] dropping unroutable '{messageType}' frame (task_id={taskId ?? "null"})");
            }
            else if (taskId is not null)
            {
                // A2: a frame for a task this client no longer tracks (the submit
                // already timed out / was cancelled) is dropped on purpose; log it
                // so the drop is observable instead of silent.
                Debug.WriteLine($"[ipc] dropping late '{messageType}' frame for unknown task {taskId}");
            }

            return;
        }

        switch (messageType)
        {
            case "accepted":
                break;

            case "progress":
                var detail = MapProgress(root);
                task.LastProgressAt = DateTimeOffset.UtcNow;
                task.LastStage = detail.Stage;
                task.LastSubStage = detail.SubStage;
                task.LastFraction = detail.Fraction;
                // D2/L4: the watchdog only guards the sampling phase. On the first
                // sampling frame, arm it and disable the load timeout (the sampling
                // phase is unbounded; L1 is the safety net).
                if (string.Equals(detail.Stage, "sampling", StringComparison.Ordinal))
                {
                    task.SamplingArmed = true;
                    task.DisableTimeout();
                }

                ProgressReceived?.Invoke(detail);
                task.Progress?.Report(new InferenceProgress
                {
                    Fraction = detail.Fraction,
                    Message = detail.Message,
                });

                break;

            case "preview":
                task.LastProgressAt = DateTimeOffset.UtcNow;
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
                var code = ReadString(root, "code") ?? "backend_error";
                var message = ReadString(root, "message") ?? "Inference backend returned an error.";
                task.Completion.TrySetException(new InferenceBackendException(code, message));
                // L2 (D1): a Dynamic-VRAM mechanism failure (AcceleratorError / out of
                // memory) is not fixed by the resolution fallback — run the same
                // cancel + restart recovery as the L1 watchdog. Offloaded so the
                // receive loop never blocks on the cancel-ack wait.
                if (IsMechanismFailure(message))
                {
                    _ = Task.Run(() => HandleStuckAsync(task, "accelerator_error"));
                }

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

    /// <summary>
    /// B8: resolves the task a task_id-less <c>error</c> frame belongs to. A frame that carries a
    /// task_id is left alone (an id not in the table is a late/unknown frame and stays dropped);
    /// a task_id-less frame falls back to the single in-flight task (Z18 single-slot).
    /// </summary>
    internal static string? ResolveErrorTargetTaskId(string? frameTaskId, string? activeTaskId)
        => frameTaskId ?? activeTaskId;

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

        /// <summary>When the submit was accepted (L3 <c>elapsed_since_accepted_ms</c>).</summary>
        public DateTimeOffset AcceptedAt { get; set; }

        /// <summary>Last time a <c>progress</c> / <c>preview</c> frame touched this task (L1).</summary>
        public DateTimeOffset LastProgressAt { get; set; }

        /// <summary>Last progress fields, kept for the L3 failure log.</summary>
        public string? LastStage { get; set; }

        public string? LastSubStage { get; set; }

        public double? LastFraction { get; set; }

        /// <summary>True once the first <c>stage=="sampling"</c> frame arrived (D2: watchdog armed only for sampling).</summary>
        public bool SamplingArmed { get; set; }

        /// <summary>0/1 latch so the watchdog recovers a given task at most once.</summary>
        public int StuckRecoveryFlag;

        /// <summary>
        /// The submit's timeout source; the receive loop disables it on the first
        /// sampling frame (L4). Null until <c>SubmitEditAsync</c> creates it.
        /// </summary>
        public CancellationTokenSource? Timeout { get; set; }

        /// <summary>Disables the submit timeout (L4: sampling is unbounded; L1 is the safety net).</summary>
        public void DisableTimeout()
        {
            try
            {
                Timeout?.CancelAfter(System.Threading.Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                // The submit already completed and disposed the source.
            }
        }
    }
}
