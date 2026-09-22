using ZivAiEditor.Agent;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 6 execution queue tests (no GPU): the single-slot queue must never run
/// two work items at once (Z18).
/// </summary>
public class ExecutionQueueTests
{
    [Fact]
    public async Task RunAsync_Never_Runs_Two_Items_Concurrently()
    {
        using var queue = new ExecutionQueue();
        var concurrent = 0;
        var maxConcurrent = 0;

        var tasks = Enumerable.Range(0, 5).Select(_ => queue.RunAsync(async ct =>
        {
            var current = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, current);
            await Task.Delay(25, ct);
            Interlocked.Decrement(ref concurrent);
            return true;
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, maxConcurrent);
        Assert.Equal(0, concurrent);
    }

    [Fact]
    public async Task RunAsync_Returns_Work_Result()
    {
        using var queue = new ExecutionQueue();

        var result = await queue.RunAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            Interlocked.CompareExchange(ref target, value, current);
        }
    }
}
