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
}
