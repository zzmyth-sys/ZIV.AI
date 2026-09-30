using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// N1: the IPC client warns (but never rejects / degrades) when the backend's pong reports a
/// protocol_version different from this build's expectation. Headless: a connected in-process
/// named-pipe pair, no GPU / no Python.
/// </summary>
[SupportedOSPlatform("windows")]
public class ProtocolVersionTests
{
    private static PythonBackendOptions NewOptions() => new()
    {
        PipeName = "zivai.pver." + Guid.NewGuid().ToString("N"),
        PythonExe = "python.exe",
        Script = "main.py",
        AutoRestartEnabled = false,
        RequestTimeoutMs = 2_000,
    };

    private static async Task<string?> RunPongAsync(string protocolVersion)
    {
        var options = NewOptions();
        using var server = new NamedPipeServerStream(
            options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var peer = new NamedPipeClientStream(
            ".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var connecting = server.WaitForConnectionAsync();
        await peer.ConnectAsync(5000);
        await connecting;

        await using var manager = new PythonProcessManager(options);
        manager.EnsureStartedOverride = _ => Task.FromResult<Stream>(server);
        using var client = new IpcInferenceClient(manager);

        var peerTask = Task.Run(async () =>
        {
            var ping = await IpcFraming.ReadFrameAsync(peer, CancellationToken.None);
            using var doc = JsonDocument.Parse(ping!.Value.Payload);
            var requestId = doc.RootElement.GetProperty("request_id").GetString();
            var json =
                $"{{\"type\":\"pong\",\"request_id\":\"{requestId}\",\"status\":\"ok\",\"protocol_version\":\"{protocolVersion}\"}}";
            await IpcFraming.WriteJsonAsync(peer, json, CancellationToken.None);
        });

        var health = await client.CheckHealthAsync();
        Assert.NotNull(health);
        await peerTask.WaitAsync(TimeSpan.FromSeconds(5));
        return client.LastProtocolVersionWarning;
    }

    [Fact]
    public async Task Mismatched_Protocol_Version_Is_Warned_Not_Rejected()
    {
        var warning = await RunPongAsync("0.8");

        Assert.NotNull(warning);
        Assert.Contains("0.8", warning);
        Assert.Contains("0.9", warning);
    }

    [Fact]
    public async Task Matching_Protocol_Version_Does_Not_Warn()
    {
        var warning = await RunPongAsync(IpcInferenceClient.ExpectedProtocolVersion);

        Assert.Null(warning);
    }
}
