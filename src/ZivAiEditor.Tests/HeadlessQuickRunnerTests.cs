using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZivAiEditor.App;
using ZivAiEditor.Contracts.Enums;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.UI;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// Bridge §7.2: HeadlessQuickRunner control flow with a fake IQuickRunHost — success / error /
/// busy / forwarded-busy all write notify. No Python, no GPU (Z30).
/// </summary>
public class HeadlessQuickRunnerTests
{
    private static string TempDir()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zivai_headless_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static LaunchOptions Options(string notify, string output)
        => new()
        {
            ImagePath = @"C:\img\source.png",
            QuickTemplateId = "/去水印",
            OutputPath = output,
            NotifyPath = notify,
        };

    private static NotifyMessage ReadNotify(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return new NotifyMessage
        {
            Status = root.GetProperty("status").GetString()!,
            OutputPath = root.TryGetProperty("output_path", out var o) ? o.GetString() : null,
            Error = root.TryGetProperty("error", out var e) ? e.GetString() : null,
            Exited = root.GetProperty("exited").GetBoolean(),
        };
    }

    [Fact]
    public async Task Success_WritesSuccessNotify_AndDisposesHost()
    {
        var dir = TempDir();
        var notify = Path.Combine(dir, "n.json");
        var output = Path.Combine(dir, "out.png");
        var host = new FakeHost
        {
            Parsed = new ParseResult { Success = true, Plan = new EditPlan { MainImagePath = @"C:\img\source.png" } },
            State = new TaskState { Status = TaskStatus.Succeeded, OutputImagePath = output },
        };
        var lockPath = Path.Combine(dir, "engine.lock");

        using var engineLock = new EngineLock(lockPath);
        var code = await HeadlessQuickRunner.RunAsync(
            Options(notify, output), new FakeShell(), engineLock, () => host);

        Assert.Equal(0, code);
        Assert.True(host.Disposed);
        var message = ReadNotify(notify);
        Assert.Equal("success", message.Status);
        Assert.Equal(output, message.OutputPath);
        Assert.True(message.Exited);
    }

    [Fact]
    public async Task ExecutionFailure_WritesErrorNotify()
    {
        var dir = TempDir();
        var notify = Path.Combine(dir, "n.json");
        var host = new FakeHost
        {
            Parsed = new ParseResult { Success = true, Plan = new EditPlan() },
            State = new TaskState { Status = TaskStatus.Failed, ErrorMessage = "boom" },
        };

        using var engineLock = new EngineLock(Path.Combine(dir, "engine.lock"));
        var code = await HeadlessQuickRunner.RunAsync(
            Options(notify, Path.Combine(dir, "o.png")), new FakeShell(), engineLock, () => host);

        Assert.Equal(1, code);
        var message = ReadNotify(notify);
        Assert.Equal("error", message.Status);
        Assert.Contains("boom", message.Error);
    }

    [Fact]
    public async Task ParseException_WritesErrorNotify_AndDisposesHost()
    {
        var dir = TempDir();
        var notify = Path.Combine(dir, "n.json");
        var host = new FakeHost { ThrowOnParse = true };

        using var engineLock = new EngineLock(Path.Combine(dir, "engine.lock"));
        var code = await HeadlessQuickRunner.RunAsync(
            Options(notify, Path.Combine(dir, "o.png")), new FakeShell(), engineLock, () => host);

        Assert.Equal(1, code);
        Assert.True(host.Disposed);
        Assert.Equal("error", ReadNotify(notify).Status);
    }

    [Fact]
    public async Task Busy_WhenLockHeld_DoesNotBuildHost()
    {
        var dir = TempDir();
        var notify = Path.Combine(dir, "n.json");
        var lockPath = Path.Combine(dir, "engine.lock");

        var holder = new EngineLock(lockPath);
        Assert.True(holder.TryAcquire());

        var factoryCalled = false;
        var code = await HeadlessQuickRunner.RunAsync(
            Options(notify, Path.Combine(dir, "o.png")),
            new FakeShell(),
            new EngineLock(lockPath),
            () =>
            {
                factoryCalled = true;
                return new FakeHost();
            });

        holder.Dispose();
        Assert.Equal(1, code);
        Assert.False(factoryCalled);
        Assert.Equal("busy", ReadNotify(notify).Status);
    }

    [Fact]
    public async Task ForwardedRequest_WhileRunning_GetsBusyNotify()
    {
        var dir = TempDir();
        var notify = Path.Combine(dir, "n.json");
        var forwardedNotify = Path.Combine(dir, "forwarded.json");
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var host = new FakeHost
        {
            OnParse = async (_, _, _, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return new ParseResult { Success = true, Plan = new EditPlan() };
            },
            State = new TaskState { Status = TaskStatus.Succeeded, OutputImagePath = Path.Combine(dir, "o.png") },
        };

        var shell = new FakeShell();
        using var engineLock = new EngineLock(Path.Combine(dir, "engine.lock"));
        var run = HeadlessQuickRunner.RunAsync(
            Options(notify, Path.Combine(dir, "o.png")), shell, engineLock, () => host);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shell.Raise(new LaunchOptions
        {
            QuickTemplateId = "/全景",
            ImagePath = @"C:\img\other.png",
            NotifyPath = forwardedNotify,
        });

        var forwarded = ReadNotify(forwardedNotify);
        Assert.Equal("busy", forwarded.Status);

        release.TrySetResult();
        Assert.Equal(0, await run);
    }

    private sealed class FakeShell : IShellContext
    {
        public BackendSettings LoadSettings() => new();

        public string TemplateDirectory => Path.GetTempPath();

        public event Action<LaunchOptions>? LaunchRequested;

        public void Raise(LaunchOptions options) => LaunchRequested?.Invoke(options);
    }

    private sealed class FakeHost : IQuickRunHost
    {
        public ParseResult? Parsed { get; init; }

        public TaskState? State { get; init; }

        public bool ThrowOnParse { get; init; }

        public bool Disposed { get; private set; }

        public Func<string, string?, ResolutionPolicy?, CancellationToken, Task<ParseResult>>? OnParse { get; init; }

        public void SetRoot(string imagePath)
        {
        }

        public ResolutionPolicy? ResolveResolution(string? tier) => null;

        public Task<ParseResult> ParseAsync(
            string template,
            string? outputPath,
            ResolutionPolicy? resolution,
            CancellationToken ct)
        {
            if (ThrowOnParse)
            {
                throw new InvalidOperationException("parse exploded");
            }

            if (OnParse is not null)
            {
                return OnParse(template, outputPath, resolution, ct);
            }

            return Task.FromResult(Parsed!);
        }

        public Task<TaskState> ExecuteAsync(EditPlan plan, CancellationToken ct)
            => Task.FromResult(State!);

        public void Dispose() => Disposed = true;
    }
}
