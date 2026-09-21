using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ZivAiEditor.Backend;

public sealed class PythonBackendOptions
{
    public string PipeName { get; init; } = "zivai.infer.v1";

    public string PythonExe { get; init; } = @"D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe";

    public string Script { get; init; } = @"D:\devlop\ZIV.AI\python\server\main.py";

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

    public int BufferSize { get; init; } = 1 << 20;

    /// <summary>
    /// Extra environment variables for the Python process (used by tests to
    /// shorten the idle / heartbeat intervals).
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; }
        = new Dictionary<string, string>();

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
    }

    public PythonBackendOptions Options { get; }

    public string PipePath => Options.PipePath;

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
                return _pipe;
            }

            await StopCoreAsync().ConfigureAwait(false);

            var pipe = CreateSecurePipeServer(Options.PipeName, Options.BufferSize);
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
