using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ZivAiEditor.Backend;

/// <summary>
/// Lifecycle half of <see cref="PythonProcessManager"/> (Z8 split): start / stop / dispose and
/// the process-start plumbing (start-info build, secure pipe creation, output capture). Split
/// out of <c>PythonProcessManager.cs</c> to keep each file within the Z8 budget; this is a pure
/// partial move plus the B16 pipe-dispose fix in <see cref="EnsureStartedAsync"/>.
/// </summary>
public sealed partial class PythonProcessManager
{
    public async Task<Stream> EnsureStartedAsync(CancellationToken ct = default)
    {
        if (EnsureStartedOverride is { } ensureOverride)
            return await ensureOverride(ct).ConfigureAwait(false);
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

            // B16: build the start info (validates the script; may throw) before any resource is
            // created, then dispose the pipe / process if starting fails for any reason.
            var startInfo = BuildStartInfo();
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += (_, e) => AppendOutput("out", e.Data);
            process.ErrorDataReceived += (_, e) => AppendOutput("err", e.Data);

            NamedPipeServerStream pipe = null!;
            try
            {
                pipe = CreateSecurePipeServer(_pipeName, Options.BufferSize);
                _pipe = pipe;

                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start the Python backend process.");
                }
            }
            catch
            {
                _pipe = null;
                pipe?.Dispose();
                process.Dispose();
                throw;
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
