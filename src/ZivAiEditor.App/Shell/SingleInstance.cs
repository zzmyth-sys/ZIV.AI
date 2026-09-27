using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
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
    /// <summary>
    /// Upper bound on a single hand-off line (chars). A normal <see cref="LaunchOptions"/>
    /// is a few hundred bytes; anything larger is discarded without buffering it unboundedly
    /// (Batch 2A / D3 hardening against a malicious oversized pipe write).
    /// </summary>
    private const int MaxPayloadChars = 64 * 1024;

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
                using var server = CreateSecureServer(_pipeName);
                using var reader = new StreamReader(server, Encoding.UTF8);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var line = await ReadBoundedLineAsync(reader, token).ConfigureAwait(false);
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

    /// <summary>
    /// Creates the listening pipe with a DACL restricted to the current user (Batch 2A / D3,
    /// mirrors <c>PythonProcessManager.CreateSecurePipeServer</c>): another local user cannot
    /// connect and inject <see cref="LaunchOptions"/>.
    /// </summary>
    private static NamedPipeServerStream CreateSecureServer(string pipeName)
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Unable to resolve the current user SID.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity: security);
    }

    /// <summary>
    /// Reads one UTF-8 line up to <see cref="MaxPayloadChars"/> chars (Batch 2A / D3). Returns
    /// <c>null</c> on EOF-before-newline or when the line exceeds the cap (the oversized line is
    /// discarded, not buffered). Cancellation propagates to the caller's catch.
    /// </summary>
    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            if (read == 0)
            {
                // Client closed before a newline; treat a partial line as the line, else EOF.
                return builder.Length > 0 ? builder.ToString() : null;
            }

            for (var i = 0; i < read; i++)
            {
                var ch = buffer[i];
                if (ch == '\n')
                {
                    return builder.ToString().TrimEnd('\r');
                }

                if (builder.Length >= MaxPayloadChars)
                {
                    return null; // oversized: discard rather than grow without bound
                }

                builder.Append(ch);
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
