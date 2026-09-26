using System;
using System.IO;
using System.Text.Json;
using Xunit;
using ZivAiEditor.App;

namespace ZivAiEditor.Tests;

/// <summary>Bridge §5 / §9: EngineLock + NotifyWriter (App layer, file IO only, no GPU).</summary>
public class BridgeEngineLockAndNotifyTests
{
    private static string TempPath(string name)
        => Path.Combine(Path.GetTempPath(), "zivai_bridge_" + Guid.NewGuid().ToString("N"), name);

    [Fact]
    public void EngineLock_Acquire_Release_And_TryAcquire()
    {
        var path = TempPath("engine.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var first = new EngineLock(path);
        Assert.True(first.TryAcquire());
        Assert.True(first.IsHeld);

        var second = new EngineLock(path);
        Assert.False(second.TryAcquire());

        first.Release();
        Assert.False(first.IsHeld);
        Assert.True(second.TryAcquire());

        second.Dispose();
    }

    [Fact]
    public void EngineLock_Probe_DoesNotCreateTheFile()
    {
        var path = TempPath("engine.lock");

        Assert.False(EngineLock.IsBusy(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void EngineLock_Dispose_DeletesHeldFile_ButNotPeerFile()
    {
        var path = TempPath("engine.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");

        var loser = new EngineLock(path);
        loser.Dispose(); // never held → must NOT delete a peer's file
        Assert.True(File.Exists(path));

        var holder = new EngineLock(path);
        Assert.True(holder.TryAcquire());
        holder.Dispose();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void NotifyWriter_WritesAtomicSnakeCase_WithExitedFlag()
    {
        var path = TempPath("quick.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        NotifyWriter.TryWrite(path, new NotifyMessage
        {
            Status = NotifyStatus.Success,
            SourceImage = @"C:\a.png",
            Template = "/去水印",
            OutputPath = @"C:\out.png",
            Error = null,
            ElapsedMs = 12345,
            Exited = true,
        });

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp")); // atomic move leaves no temp behind

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("success", root.GetProperty("status").GetString());
        Assert.Equal("/去水印", root.GetProperty("template").GetString());
        Assert.Equal(12345, root.GetProperty("elapsed_ms").GetInt64());
        Assert.True(root.GetProperty("exited").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("error")]
    [InlineData("cancelled")]
    [InlineData("busy")]
    public void NotifyWriter_WritesEveryStatus(string status)
    {
        var path = TempPath("status.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        NotifyWriter.TryWrite(path, new NotifyMessage { Status = status, Exited = false });

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(status, document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.GetProperty("exited").GetBoolean());
    }
}
