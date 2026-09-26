using System.Diagnostics;
using System.Text.Json;

namespace ZivAiEditor.Backend;

/// <summary>
/// L1 stuck watchdog + L2 mechanism-failure recovery + L3 failure log (Step 9C.20).
/// Pure observation of the receive/state machine: it never runs GPU work and only
/// reacts to missing progress (L1) or a Dynamic-VRAM error frame (L2).
/// </summary>
public sealed partial class IpcInferenceClient
{
    /// <summary>L2 (D1): substrings that mark a Dynamic-VRAM mechanism failure (not a slowdown).</summary>
    private static readonly string[] MechanismFailureMarkers = { "acceleratorerror", "out of memory" };

    /// <summary>Test seam (instance): overrides the L3 failure log path; null = <c>_cache/backend_failure.log</c>.</summary>
    internal string? FailureLogPath { get; set; }

    /// <summary>
    /// Raised once per recovery (L1 stuck / L2 mechanism error) <b>after</b> the
    /// action was taken. The App subscribes to surface "生成失败，已重启后端".
    /// </summary>
    public event Action<StuckRecoveryInfo>? StuckRecoveryTriggered;

    private double? _lastVramMb;
    private int _watchdogStarted;

    /// <summary>Details of one L1/L2 recovery, for the UI bubble.</summary>
    public sealed record StuckRecoveryInfo(string TaskId, string Reason, string Action);

    /// <summary>Starts the L1 watchdog once (idempotent). Called from the constructor.</summary>
    internal void EnsureWatchdogStarted()
    {
        if (Interlocked.Exchange(ref _watchdogStarted, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() => WatchdogLoopAsync(_lifetime.Token));
    }

    private async Task WatchdogLoopAsync(CancellationToken ct)
    {
        var stuckMs = Math.Max(250, Process.Options.StuckTimeoutMs);
        // 5 s at the 60 s default; scales down so a small (test) StuckTimeoutMs stays responsive.
        var pollMs = Math.Max(250, Math.Min(5000, stuckMs / 4));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(pollMs, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Cancelled / source disposed: the client is going away.
                break;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _tasks)
            {
                var task = pair.Value;
                // D2: only the sampling phase is watched; the load phase is governed
                // by ModelLoadTimeoutMs.
                if (!task.SamplingArmed)
                {
                    continue;
                }

                if ((now - task.LastProgressAt).TotalMilliseconds <= stuckMs)
                {
                    continue;
                }

                if (Interlocked.Exchange(ref task.StuckRecoveryFlag, 1) == 1)
                {
                    continue;
                }

                // Offload so the poll loop is not blocked by the cancel-ack wait.
                _ = Task.Run(() => HandleStuckAsync(task, "stuck"));
            }
        }
    }

    /// <summary>
    /// L1/L2 action: log → forward cancel → wait for the ack window → restart when
    /// the cancel was not sent/acknowledged → raise <see cref="StuckRecoveryTriggered"/>.
    /// The restart respects <c>AutoRestartEnabled</c> / the attempt budget.
    /// </summary>
    private async Task HandleStuckAsync(PendingTask task, string reason)
    {
        var action = "restart";
        try
        {
            var stream = _receiveStream;
            if (stream is not null)
            {
                var (sent, acked) = await TryForwardCancelAsync(
                    stream, task.TaskId, task, Process.Options.CancelConfirmTimeoutMs).ConfigureAwait(false);
                if (sent && acked)
                {
                    action = "cancel_ok";
                }
            }

            if (action != "cancel_ok")
            {
                _ = Process.RequestRestartAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ipc] stuck recovery failed: {ex.Message}");
        }

        RecordFailure(reason, task, action);
        StuckRecoveryTriggered?.Invoke(new StuckRecoveryInfo(task.TaskId, reason, action));
    }

    /// <summary>L3: append one JSONL line to the failure log; never throws.</summary>
    private void RecordFailure(string reason, PendingTask task, string action)
    {
        try
        {
            double? lastVram;
            lock (_stateLock)
            {
                lastVram = _lastVramMb;
            }

            var path = FailureLogPath ?? Path.Combine(AppContext.BaseDirectory, "_cache", "backend_failure.log");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var record = new Dictionary<string, object?>
            {
                ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["reason"] = reason,
                ["last_progress"] = new Dictionary<string, object?>
                {
                    ["stage"] = task.LastStage,
                    ["sub_stage"] = task.LastSubStage,
                    ["fraction"] = task.LastFraction,
                    ["ts"] = task.LastProgressAt.ToUnixTimeMilliseconds(),
                },
                ["last_vram_mb"] = lastVram,
                ["elapsed_since_accepted_ms"] = Math.Round((DateTimeOffset.UtcNow - task.AcceptedAt).TotalMilliseconds, 1),
                ["action"] = action,
            };
            File.AppendAllText(path, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
        catch
        {
            // Observation must never affect the pipeline.
        }
    }

    /// <summary>L2 (D1): true when the backend error message is a Dynamic-VRAM mechanism failure.</summary>
    internal static bool IsMechanismFailure(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        foreach (var marker in MechanismFailureMarkers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
