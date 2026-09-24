using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ZivAiEditor.UI;

namespace ZivAiEditor.App;

/// <summary>
/// Single-instance guard for ZIV.AI (Z4: the Mutex and Named Pipe live only in the
/// App platform layer). The first process takes a per-user / per-session mutex and
/// listens on a named pipe; a later process connects, hands over its
/// <see cref="LaunchOptions"/> as a single UTF-8 JSON line, and exits.
///
/// This is unrelated to the Python backend IPC (<c>contracts/ipc-protocol.md</c>):
/// that is the inference transport (C# = server, Python = client); this is the
/// launch hand-off between two ZIV.AI processes (Z27 / Z23 — no version coupling).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>Raised on the listener thread for each request handed over by a later process.</summary>
    public event Action<LaunchOptions>? PathReceived;

    /// <summary>True when this process owns the mutex, i.e. it is the first instance.</summary>
    public bool IsFirstInstance { get; }

    /// <param name="instanceName">
    /// Overrides the derived per-user / per-session id (used by tests for isolation).
    /// </param>
    public SingleInstance(string? instanceName = null)
    {
        var id = string.IsNullOrWhiteSpace(instanceName) ? InstanceId() : instanceName!;

        // Local\ scopes the mutex to the logon session; the user SID + session id are
        // baked into the name so two users (or two sessions) never share one lock.
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{id}", out var createdNew);
        IsFirstInstance = createdNew;

        // Named pipes are machine-wide (\\.\pipe\...), so the pipe name carries SID + session too.
        _pipeName = id;

        if (IsFirstInstance)
        {
            _ = Task.Run(() => ListenAsync(_cts.Token));
        }
    }

    /// <summary>
    /// Hands a request to the running first instance. Returns <c>false</c> when nothing
    /// was accepted (the caller still exits — a dropped hand-off beats a second window).
    /// </summary>
    public bool SendToExistingInstance(LaunchOptions options)
    {
        if (options is null || options.IsEmpty)
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(2000);

            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(options, LaunchOptionsJsonContext.Default.LaunchOptions));
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ZIV-AI-SINGLE] forward failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using var reader = new StreamReader(server, Encoding.UTF8);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var options = JsonSerializer.Deserialize(line, LaunchOptionsJsonContext.Default.LaunchOptions);
                    if (options is not null)
                    {
                        PathReceived?.Invoke(options);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // A broken connection must not kill the listener; wait for the next one.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }

        // Only the instance that took the lock releases it; the OS releases it on crash anyway.
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }

        try { _mutex.Dispose(); } catch { }
    }

    /// <summary>Per-user, per-session id (user SID + session id).</summary>
    private static string InstanceId()
    {
        string sid;
        try
        {
            sid = WindowsIdentity.GetCurrent().User?.Value ?? "default";
        }
        catch
        {
            sid = "default";
        }

        var session = 0;
        try
        {
            session = Process.GetCurrentProcess().SessionId;
        }
        catch
        {
            // ignore; 0 is a safe fallback
        }

        return $"ZIV.AI.SingleInstance.{sid}.{session}";
    }
}
