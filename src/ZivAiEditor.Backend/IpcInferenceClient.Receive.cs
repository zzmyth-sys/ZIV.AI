using System.Buffers.Binary;
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
