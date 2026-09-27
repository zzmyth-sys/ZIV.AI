using ZivAiEditor.App;
using ZivAiEditor.UI;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9A single-instance tests. Each test uses a unique instance name so it never
/// collides with a running editor or another test (no GPU, no UI thread).
/// </summary>
public class SingleInstanceTests
{
    private static string UniqueName() => "ZIV.AI.SingleInstance.test." + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Second_Instance_Forwards_Payload_To_First()
    {
        var name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.IsFirstInstance);

        var received = new TaskCompletionSource<LaunchOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.PathReceived += options => received.TrySetResult(options);

        // Let the listener reach WaitForConnectionAsync before the client connects.
        await Task.Delay(200);

        using var second = new SingleInstance(name);
        Assert.False(second.IsFirstInstance);

        var payload = new LaunchOptions { ImagePath = @"C:\img\a.png", Prompt = "换背景 茶肆" };
        Assert.True(second.SendToExistingInstance(payload));

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(@"C:\img\a.png", result.ImagePath);
        Assert.Equal("换背景 茶肆", result.Prompt);
    }

    [Fact]
    public void Empty_Payload_Is_Not_Forwarded()
    {
        using var single = new SingleInstance(UniqueName());

        Assert.False(single.SendToExistingInstance(new LaunchOptions()));
    }

    [Fact]
    public async Task Oversized_Payload_Is_Discarded_And_Listener_Survives()
    {
        // Batch 2A / D3: a line above the 64K-char cap must be discarded (no forward, no
        // crash) and the listener must keep accepting normal hand-offs. The ACL itself
        // (other users rejected) needs real-machine multi-user validation — see ACCEPTANCE.
        var name = UniqueName();
        using var first = new SingleInstance(name);
        Assert.True(first.IsFirstInstance);

        const int cap = 64 * 1024;
        var oversized = new TaskCompletionSource<LaunchOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
        var normal = new TaskCompletionSource<LaunchOptions>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.PathReceived += options =>
        {
            if ((options.Prompt?.Length ?? 0) > cap)
            {
                oversized.TrySetResult(options);
            }
            else
            {
                normal.TrySetResult(options);
            }
        };

        await Task.Delay(200);

        using var second = new SingleInstance(name);
        Assert.False(second.IsFirstInstance);

        // > cap chars: the listener discards the line.
        second.SendToExistingInstance(new LaunchOptions { Prompt = new string('x', cap + 1000) });
        var racer = await Task.WhenAny(oversized.Task, Task.Delay(700));
        Assert.NotSame(oversized.Task, racer);

        // The listener survives and still accepts a normal payload.
        Assert.True(second.SendToExistingInstance(
            new LaunchOptions { ImagePath = @"C:\img\ok.png", Prompt = "换背景" }));
        var result = await normal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(@"C:\img\ok.png", result.ImagePath);
    }
}
