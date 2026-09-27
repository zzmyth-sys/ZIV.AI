using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Batch P1 / B8: routing helper for error frames that carry no <c>task_id</c>. The class name
/// deliberately avoids "Ipc" so the tests are included in the non-GPU (`!~Ipc`) baseline.
/// </summary>
public class InferenceErrorRoutingTests
{
    [Fact]
    public void Frame_With_TaskId_Keeps_Its_Own_Id()
    {
        // A frame that names a task is never re-attributed (an untracked id is a late frame).
        Assert.Equal("frame-task", IpcInferenceClient.ResolveErrorTargetTaskId("frame-task", "active-task"));
    }

    [Fact]
    public void TaskIdless_Frame_Falls_Back_To_The_Active_Task()
    {
        // Z18 single-slot: the one in-flight task is the only possible owner.
        Assert.Equal("active-task", IpcInferenceClient.ResolveErrorTargetTaskId(null, "active-task"));
    }

    [Fact]
    public void TaskIdless_Frame_With_No_Active_Task_Is_Unroutable()
    {
        Assert.Null(IpcInferenceClient.ResolveErrorTargetTaskId(null, null));
    }
}
