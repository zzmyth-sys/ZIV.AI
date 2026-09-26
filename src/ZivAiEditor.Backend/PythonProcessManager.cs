using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ZivAiEditor.Backend;

public enum PythonBackendState
{
    Stopped,
    Running,
    Restarting,
    Failed,
}

public sealed class PythonBackendOptions
{
    public string PipeName { get; init; } = "zivai.infer.v1";

    // A9: no dev-machine default; AppContext always supplies real values and tests set them.
    public string PythonExe { get; init; } = string.Empty;

    public string Script { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public int ConnectTimeoutMs { get; init; } = 30_000;

    public int ShutdownTimeoutMs { get; init; } = 5_000;

    public int RequestTimeoutMs { get; init; } = 10_000;

    /// <summary>
    /// Upper bound for the lazy model load triggered by the first submit
    /// (Step 2.2). Measured cold load is ~32 s on the RTX 4080, so this is
    /// deliberately larger than <see cref="RequestTimeoutMs"/>.
    /// </summary>
    public int ModelLoadTimeoutMs { get; init; } = 180_000;

    /// <summary>
    /// Raise <see cref="IpcInferenceClient.HeartbeatLost"/> after this long
    /// without a heartbeat frame (Step 3). Python sends every 10 s, so the
    /// default tolerates three missed beats.
    /// </summary>
    public int HeartbeatLostAfterMs { get; init; } = 30_000;

    /// <summary>
    /// Restart the backend automatically when <see cref="IpcInferenceClient.HeartbeatLost"/>
    /// fires (Step 4). Defaults to on.
    /// </summary>
    public bool AutoRestartEnabled { get; init; } = true;

    /// <summary>
    /// Consecutive lost-heartbeat restarts allowed before the backend is marked
    /// <see cref="PythonBackendState.Failed"/> and no further restart is tried.
    /// The counter resets after a successful submit.
    /// </summary>
    public int MaxRestartAttempts { get; init; } = 3;

    /// <summary>
    /// Base backoff between restart attempts; grows exponentially
    /// (2s / 4s / 8s with the default).
    /// </summary>
    public int RestartBackoffMs { get; init; } = 2_000;

    public int BufferSize { get; init; } = 1 << 20;

    /// <summary>
    /// Extra environment variables for the Python process (used by tests to
    /// shorten the idle / heartbeat intervals).
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Optional path for the backend's own log file (Step 9C.6-D diagnostic). When set,
    /// the Python process is launched with <c>--log-file</c> so its submit-resolution /
    /// error log survives the in-memory capture. <c>null</c> disables file logging.
    /// </summary>
    public string? LogFilePath { get; init; }

    public string PipePath => @"\\.\pipe\" + PipeName;
}

[SupportedOSPlatform("windows")]
public sealed class PythonProcessManager : IDisposable, IAsyncDisposable
{
    private const int MaxOutputChars = 16_000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _outputLock = new();
    private readonly StringBuilder _output = new();

    private NamedPipeServerStream? _pipe;
    private Process? _process;
    private bool _disposed;
    private string _pipeName;
    private int _restartSeq;
    private int _restartAttempts;
    private int _restarting;
    private PythonBackendState _state = PythonBackendState.Stopped;
    private IpcInferenceClient? _client;

    public PythonProcessManager(PythonBackendOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.PipeName))
        {
            throw new ArgumentException("PipeName is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.PythonExe))
        {
            throw new ArgumentException("PythonExe is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Script))
        {
            throw new ArgumentException("Script is required.", nameof(options));
        }

        _pipeName = options.PipeName;
    }

    public PythonBackendOptions Options { get; }

    public string PipePath => @"\\.\pipe\" + _pipeName;

    public PythonBackendState State
    {
        get
        {
            lock (_outputLock)
            {
                return _state;
            }
        }
    }

    /// <summary>Raised just before the manager tears the old backend down.</summary>
    public event Action? Restarting;

    /// <summary>Raised after a restart reaches a healthy (ping-able) backend.</summary>
    public event Action? Restarted;

    /// <summary>Raised once the restart budget is exhausted; no further restart is tried.</summary>
    public event Action<Exception>? RestartFailed;

    /// <summary>
    /// Wire an <see cref="IpcInferenceClient"/> so this manager can react to
    /// <see cref="IpcInferenceClient.HeartbeatLost"/> with an auto restart
    /// (Step 4). No-op when <see cref="PythonBackendOptions.AutoRestartEnabled"/>
    /// is false.
    /// </summary>
    public void AttachInferenceClient(IpcInferenceClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!Options.AutoRestartEnabled)
        {
            return;
        }

        _client = client;
        client.HeartbeatLost += OnHeartbeatLost;
    }

    public void DetachInferenceClient(IpcInferenceClient client)
    {
        if (ReferenceEquals(_client, client))
        {
            client.HeartbeatLost -= OnHeartbeatLost;
            _client = null;
        }
    }

    /// <summary>
    /// Called by the client after a submit completes successfully; clears the
    /// consecutive-restart counter so an occasional crash does not accumulate
    /// toward <see cref="PythonBackendOptions.MaxRestartAttempts"/>.
    /// </summary>
    public void NotifyTaskSucceeded()
    {
        lock (_outputLock)
        {
            if (_state != PythonBackendState.Failed)
            {
                _restartAttempts = 0;
            }
        }
    }

    private void OnHeartbeatLost() => _ = RequestRestartAsync();

    /// <summary>
    /// Restart the backend once: stop the old process tree, use a fresh pipe
    /// name, back off, spawn again and verify with a ping. Serialized and
    /// single-flight; returns false when disabled, already failed, or racing.
    /// </summary>
    public async Task<bool> RequestRestartAsync()
    {
        if (!Options.AutoRestartEnabled || _disposed)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            while (true)
            {
                int attempt;
                bool exhausted;
                lock (_outputLock)
                {
                    if (_state == PythonBackendState.Failed)
                    {
                        return false;
                    }

                    attempt = ++_restartAttempts;
                    exhausted = attempt > Options.MaxRestartAttempts;
                    _state = exhausted ? PythonBackendState.Failed : PythonBackendState.Restarting;
                }

                if (exhausted)
                {
                    RestartFailed?.Invoke(new InvalidOperationException(
                        $"Backend restart budget exhausted ({Options.MaxRestartAttempts} attempts)."));
                    return false;
                }

                Restarting?.Invoke();
                await StopAsync().ConfigureAwait(false);

                _pipeName = $"zivai.infer.{Environment.ProcessId}.{++_restartSeq}";

                var backoff = Options.RestartBackoffMs * (1 << (attempt - 1));
                if (backoff > 0)
                {
                    await Task.Delay(backoff).ConfigureAwait(false);
                }

                try
                {
                    await EnsureStartedAsync().ConfigureAwait(false);
                    var client = _client;
                    if (client is not null)
                    {
                        await client.CheckHealthAsync().ConfigureAwait(false);
                    }

                    lock (_outputLock)
                    {
                        _state = PythonBackendState.Running;
                    }

                    Restarted?.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    if (attempt >= Options.MaxRestartAttempts)
                    {
                        lock (_outputLock)
                        {
                            _state = PythonBackendState.Failed;
                        }

                        RestartFailed?.Invoke(ex);
                        return false;
                    }

                    lock (_outputLock)
                    {
                        _state = PythonBackendState.Stopped;
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _restarting, 0);
        }
    }

    public bool IsPipeConnected => _pipe is { IsConnected: true };

    public bool IsProcessRunning
    {
        get
        {
            var process = _process;
            if (process is null)
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public int? ProcessId => _process?.Id;

    public string CurrentOutput
    {
        get
        {
            lock (_outputLock)
            {
                return _output.ToString();
            }
        }
    }

    public async Task<Stream> EnsureStartedAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pipe is { IsConnected: true })
            {
                lock (_outputLock)
                {
                    _state = PythonBackendState.Running;
                }

                return _pipe;
            }

            await StopCoreAsync().ConfigureAwait(false);

            var pipe = CreateSecurePipeServer(_pipeName, Options.BufferSize);
            _pipe = pipe;

            var process = new Process
            {
                StartInfo = BuildStartInfo(),
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += (_, e) => AppendOutput("out", e.Data);
            process.ErrorDataReceived += (_, e) => AppendOutput("err", e.Data);

            if (!process.Start())
            {
                _pipe = null;
                pipe.Dispose();
                throw new InvalidOperationException("Failed to start the Python backend process.");
            }

            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Options.ConnectTimeoutMs);
            try
            {
                await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var output = CurrentOutput;
                await StopCoreAsync().ConfigureAwait(false);
                throw new TimeoutException(
                    $"Python backend did not connect to '{PipePath}' within {Options.ConnectTimeoutMs} ms."
                    + FormatOutput(output));
            }
            catch
            {
                await StopCoreAsync().ConfigureAwait(false);
                throw;
            }

            lock (_outputLock)
            {
                _state = PythonBackendState.Running;
            }

            return pipe;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task StopCoreAsync()
    {
        var pipe = _pipe;
        var process = _process;
        _pipe = null;
        _process = null;

        lock (_outputLock)
        {
            if (_state is not (PythonBackendState.Restarting or PythonBackendState.Failed))
            {
                _state = PythonBackendState.Stopped;
            }
        }

        if (pipe is { IsConnected: true })
        {
            await IpcFraming.TryWriteShutdownAsync(pipe).ConfigureAwait(false);
        }

        if (pipe is not null)
        {
            try
            {
                pipe.Dispose();
            }
            catch
            {
            }
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(Options.ShutdownTimeoutMs);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    try
                    {
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private ProcessStartInfo BuildStartInfo()
    {
        // B: validate the script up front so a missing / misconfigured main.py produces a
        // readable error instead of the Win32 "directory name is invalid" from Process.Start.
        PythonScriptValidator.Validate(Options.Script);

        var startInfo = new ProcessStartInfo
        {
            FileName = Options.PythonExe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Options.WorkingDirectory
                ?? Path.GetDirectoryName(Options.Script)
                ?? Environment.CurrentDirectory,
        };
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add(Options.Script);
        startInfo.ArgumentList.Add("--pipe-name");
        startInfo.ArgumentList.Add(PipePath);
        startInfo.ArgumentList.Add("--log-level");
        startInfo.ArgumentList.Add("INFO");
        if (!string.IsNullOrWhiteSpace(Options.LogFilePath))
        {
            try
            {
                var directory = Path.GetDirectoryName(Options.LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                startInfo.ArgumentList.Add("--log-file");
                startInfo.ArgumentList.Add(Options.LogFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[backend] log-file setup failed: {ex.Message}");
            }
        }

        foreach (var pair in Options.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private static NamedPipeServerStream CreateSecurePipeServer(string pipeName, int bufferSize)
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Unable to resolve the current user SID.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: bufferSize,
            outBufferSize: bufferSize,
            pipeSecurity: security);
    }

    private void AppendOutput(string channel, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_outputLock)
        {
            _output.Append('[').Append(channel).Append("] ").AppendLine(line);
            if (_output.Length > MaxOutputChars)
            {
                _output.Remove(0, _output.Length - MaxOutputChars);
            }
        }
    }

    private static string FormatOutput(string output)
        => string.IsNullOrWhiteSpace(output)
            ? string.Empty
            : Environment.NewLine + "--- python output ---" + Environment.NewLine + output;
}
