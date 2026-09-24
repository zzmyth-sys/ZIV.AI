using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.UI;
using ZivAiEditor.UI.Chat;
using ZivAiEditor.UI.Editing;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>Step 9A chat-flow tests (view model only, mock executor, no GPU).</summary>
public class SessionViewModelTests
{
    private const string Root = @"C:\img\root.png";
    private const string Output = @"C:\img\out.png";

    private static CommandParser ParserWithoutFile()
        => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"));

    private sealed class FakeExecutor : IExecutor
    {
        private readonly string? _output;
        private readonly bool _success;

        public FakeExecutor(string? output, bool success = true)
        {
            _output = output;
            _success = success;
        }

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => Task.FromResult(new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = _success ? TaskStatus.Succeeded : TaskStatus.Failed,
                Plan = plan,
                OutputImagePath = _output,
                ErrorMessage = _success ? null : "模拟失败",
            });

        public Task<TaskState> RerunAsync(
            string taskId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>Captures the plan passed to the executor so a test can inspect it.</summary>
    private sealed class CapturingExecutor : IExecutor
    {
        private readonly string? _output;

        public CapturingExecutor(string? output) => _output = output;

        public EditPlan? LastPlan { get; private set; }

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
        {
            LastPlan = plan;
            return Task.FromResult(new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = TaskStatus.Succeeded,
                Plan = plan,
                OutputImagePath = _output,
            });
        }

        public Task<TaskState> RerunAsync(
            string taskId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>An executor that stays in flight until <see cref="Complete"/> is called.</summary>
    private sealed class DeferredExecutor : IExecutor
    {
        private readonly TaskCompletionSource<TaskState> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => _completion.Task;

        public void Complete(TaskState state) => _completion.TrySetResult(state);

        public Task<TaskState> RerunAsync(
            string taskId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>Fails the first submit with a CUDA-OOM message, then succeeds (Step 9C.6-D).</summary>
    private sealed class OomOnceExecutor : IExecutor
    {
        private readonly string _output;
        private int _calls;

        public OomOnceExecutor(string output) => _output = output;

        public int Calls => _calls;

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
        {
            _calls++;
            return Task.FromResult(_calls == 1
                ? new TaskState
                {
                    TaskId = Guid.NewGuid().ToString("N"),
                    Status = TaskStatus.Failed,
                    Plan = plan,
                    ErrorMessage = "AcceleratorError: CUDA error: out of memory",
                }
                : new TaskState
                {
                    TaskId = Guid.NewGuid().ToString("N"),
                    Status = TaskStatus.Succeeded,
                    Plan = plan,
                    OutputImagePath = _output,
                });
        }

        public Task<TaskState> RerunAsync(
            string taskId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    [Fact]
    public async Task Submit_Retries_Once_After_Cuda_Oom()
    {
        var session = new EditSession();
        var executor = new OomOnceExecutor(Output);
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });

        var ok = await vm.SubmitAsync("/去水印");

        Assert.True(ok);
        Assert.Equal(2, executor.Calls);
        Assert.Equal(2, vm.History.Count);
        Assert.Contains(vm.Messages, m => m.ImagePath == Output);
        Assert.DoesNotContain(vm.Messages, m => m.IsError);
    }

    [Fact]
    public async Task Submit_Appends_Node_And_Refreshes_History()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        var ok = await vm.SubmitAsync("/去水印");

        Assert.True(ok);
        Assert.Equal(2, vm.History.Count);
        Assert.Equal("原图", vm.History[0].Node.Command);
        Assert.Equal(Output, session.GetCurrentImagePath());
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.User && m.Text == "/去水印");
        Assert.Contains(vm.Messages, m => m.ImagePath == Output);
    }

    [Fact]
    public async Task Submit_References_Land_On_Plan_AdditionalImages()
    {
        var session = new EditSession();
        var executor = new CapturingExecutor(Output);
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });

        var ok = await vm.SubmitAsync(
            "用 <image2> 的风格",
            additionalImages: new[] { @"C:\img\r1.png", @"C:\img\r2.png" });

        Assert.True(ok);
        Assert.NotNull(executor.LastPlan);
        Assert.Equal(
            new[] { @"C:\img\r1.png", @"C:\img\r2.png" },
            executor.LastPlan!.AdditionalImages);
    }

    [Fact]
    public async Task Submit_Truncates_To_Three_References_And_Hints_After_User_Message()
    {
        var session = new EditSession();
        var executor = new CapturingExecutor(Output);
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });

        var ok = await vm.SubmitAsync(
            "用参考图",
            additionalImages: new[] { "r1", "r2", "r3", "r4", "r5" });

        Assert.True(ok);
        Assert.Equal(new[] { "r1", "r2", "r3" }, executor.LastPlan!.AdditionalImages);
        var messages = vm.Messages.ToList();
        var userIndex = messages.FindIndex(m => m.Role == ChatRole.User && m.Text == "用参考图");
        var hintIndex = messages.FindIndex(m => m.Role == ChatRole.System && m.IsError);
        Assert.True(userIndex >= 0);
        Assert.True(hintIndex > userIndex);
    }

    [Fact]
    public async Task Submit_Without_References_Leaves_Plan_AdditionalImages_Empty()
    {
        var session = new EditSession();
        var executor = new CapturingExecutor(Output);
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });

        await vm.SubmitAsync("/去水印");

        Assert.NotNull(executor.LastPlan);
        Assert.Empty(executor.LastPlan!.AdditionalImages);
    }

    [Fact]
    public async Task Submit_Failure_Shows_Error_And_Adds_No_Node()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(null, success: false));
        vm.Start(new LaunchOptions { ImagePath = Root });

        var ok = await vm.SubmitAsync("把天空换成日落");

        Assert.False(ok);
        Assert.Single(vm.History);
        Assert.Equal("原图", vm.History[0].Node.Command);
        Assert.Contains(vm.Messages, m => m.IsError);
    }

    [Fact]
    public async Task Navigate_Switches_Current_And_Rebuilds_Chat()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");

        var nodeId = vm.History[1].Node.NodeId;

        Assert.True(vm.NavigateTo(nodeId));
        Assert.Equal(nodeId, session.CurrentNodeId);
        Assert.Contains(vm.Messages, m => m.Text == "/去水印");
        Assert.False(vm.NavigateTo("missing"));
    }

    [Fact]
    public void Empty_Session_Starts_Without_Messages()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));

        vm.Start(new LaunchOptions());

        Assert.Empty(vm.History);
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public async Task Submit_Marks_Pending_Bubble_While_Executing()
    {
        var session = new EditSession();
        var executor = new DeferredExecutor();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });

        var submit = vm.SubmitAsync("/去水印");

        // While the executor is in flight the bubble is flagged pending (the App
        // renders live preview frames into it).
        Assert.Contains(vm.Messages, m => m.IsPending);

        executor.Complete(new TaskState
        {
            TaskId = Guid.NewGuid().ToString("N"),
            Status = TaskStatus.Succeeded,
            Plan = new EditPlan(),
            OutputImagePath = Output,
        });
        await submit;

        Assert.DoesNotContain(vm.Messages, m => m.IsPending);
        Assert.Contains(vm.Messages, m => m.ImagePath == Output);
    }

    [Fact]
    public void GetParentImagePath_Is_Null_For_Root_Image()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        Assert.Null(vm.GetParentImagePath(Root));
        Assert.Null(vm.GetParentImagePath(""));
        Assert.Null(vm.GetParentImagePath(null));
        Assert.Null(vm.GetParentImagePath(@"C:\img\unknown.png"));
    }

    [Fact]
    public async Task GetParentImagePath_Returns_Root_For_Direct_Child()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");

        Assert.Equal(Root, vm.GetParentImagePath(Output));
    }

    [Fact]
    public async Task GetParentImagePath_Returns_Parent_Node_Output_For_Grandchild()
    {
        const string second = @"C:\img\out2.png";
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");

        // Branch a grandchild off the first node; its parent output is the first node.
        var firstId = session.CurrentNodeId!;
        session.AppendNode(firstId, second, "/换背景");

        Assert.Equal(Output, vm.GetParentImagePath(second));
    }

    [Fact]
    public async Task SetRootImage_Resets_Session_And_Rebuilds_Chat()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");
        Assert.Equal(2, vm.History.Count);

        vm.SetRootImage(@"C:\img\new.png");

        Assert.Equal(@"C:\img\new.png", session.RootImagePath);
        Assert.Single(vm.History);
        Assert.Equal("原图", vm.History[0].Node.Command);
        Assert.Equal(session.CurrentNodeId, vm.History[0].Node.NodeId);
        Assert.Contains(vm.Messages, m => m.ImagePath == @"C:\img\new.png");
    }

    [Fact]
    public async Task Navigate_To_Root_Node_Switches_Current_To_Root()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");

        var rootId = vm.History[0].Node.NodeId;

        Assert.True(vm.NavigateTo(rootId));
        Assert.Equal(rootId, session.CurrentNodeId);
        Assert.Equal(Root, session.GetCurrentImagePath());
        Assert.True(vm.History[0].IsCurrent);
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.ImagePath == Root);

        // The source image must not be rendered twice: the root node is covered by the
        // "起始图像" system bubble, so no separate User "原图" pair is emitted.
        Assert.DoesNotContain(vm.Messages, m => m.Role == ChatRole.User && m.Text == "原图");
        Assert.Single(vm.Messages, m => m.Role == ChatRole.System && m.ImagePath == Root);
    }

    [Fact]
    public void SetRootImage_Blank_Is_NoOp()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        vm.SetRootImage(null);
        vm.SetRootImage("   ");

        Assert.Equal(Root, session.RootImagePath);
    }

    [Fact]
    public void SetNodeCrop_Stores_Crop_Without_Adding_A_Node()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        var rootId = session.CurrentNodeId!;
        var crop = new CropSpec { X = 1, Y = 2, Width = 30, Height = 40, ResultImagePath = @"C:\img\root_crop.png" };
        vm.SetNodeCrop(rootId, crop);

        // No new node — the crop is a property of the existing node.
        Assert.Single(vm.History);
        Assert.Same(crop, vm.History[0].Node.Crop);
        Assert.Equal(@"C:\img\root_crop.png", session.GetCurrentPipelineImagePath());
        Assert.Equal(Root, session.GetCurrentImagePath());
    }

    [Fact]
    public void SetNodeCrop_Unknown_Node_Is_NoOp()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        vm.SetNodeCrop("missing", new CropSpec { Width = 10, Height = 10 });

        Assert.Single(vm.History);
        Assert.Null(vm.History[0].Node.Crop);
    }

    [Fact]
    public void SetNodeCrop_Readjust_Does_Not_Add_A_Node()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        vm.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\a.png" });
        var second = new CropSpec { Width = 20, Height = 20, ResultImagePath = @"C:\img\b.png" };
        vm.SetNodeCrop(rootId, second);

        Assert.Single(vm.History);
        Assert.Same(second, vm.History[0].Node.Crop);
        Assert.Equal(@"C:\img\b.png", session.GetCurrentPipelineImagePath());
    }

    [Fact]
    public void SetNodeCrop_Shows_Crop_Result_In_Chat()
    {
        const string cropResult = @"C:\img\root_crop.png";
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        vm.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = cropResult });

        // The chat's "起始图像" bubble now carries the crop result, not the original.
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.ImagePath == cropResult);
        Assert.DoesNotContain(vm.Messages, m => m.ImagePath == Root);
    }

    [Fact]
    public void GetParentPipelineImagePath_Is_Null_For_Root()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        Assert.Null(vm.GetParentPipelineImagePath(Root));
    }

    [Fact]
    public void PrepareAttachments_NoAttachments_NoRoot_NaturalLanguage_Allows_T2I()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        Assert.Equal(AttachmentPreparation.Ready, vm.PrepareAttachments("一只猫", Array.Empty<string>()));
    }

    [Fact]
    public void PrepareAttachments_NoAttachments_NoRoot_SlashCommand_Needs_Image()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        Assert.Equal(AttachmentPreparation.NoImage, vm.PrepareAttachments("/去水印", Array.Empty<string>()));
    }

    [Fact]
    public void PrepareAttachments_Attachments_NoRoot_Promotes_First_To_Root()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        var result = vm.PrepareAttachments("编辑", new[] { @"C:\img\a.png", @"C:\img\b.png" });

        Assert.Equal(AttachmentPreparation.Ready, result);
        Assert.Equal(@"C:\img\a.png", session.RootImagePath);
        Assert.Single(vm.History);
        Assert.Equal("原图", vm.History[0].Node.Command);
    }

    [Fact]
    public void PrepareAttachments_Attachments_WithRoot_NeedsDecision_And_Keeps_Root()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        var result = vm.PrepareAttachments("编辑", new[] { @"C:\img\new.png" });

        Assert.Equal(AttachmentPreparation.NeedsDecision, result);
        Assert.Equal(Root, session.RootImagePath);
    }

    [Fact]
    public void PrepareAttachments_NoAttachments_WithRoot_Is_Ready()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        Assert.Equal(AttachmentPreparation.Ready, vm.PrepareAttachments("编辑", Array.Empty<string>()));
    }

    [Fact]
    public void StartNewSessionFrom_Resets_Root_To_First_Attachment()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        vm.StartNewSessionFrom(new[] { @"C:\img\new.png", @"C:\img\other.png" });

        Assert.Equal(@"C:\img\new.png", session.RootImagePath);
        Assert.Single(vm.History);
    }

    [Fact]
    public void CanSend_Validation_Matrix()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));

        Assert.False(vm.CanSend("", 0));
        Assert.False(vm.CanSend("   ", 1));
        Assert.True(vm.CanSend("x", 0));

        vm.Mode = ImageEditMode.Single;
        Assert.True(vm.CanSend("x", 1));
        Assert.False(vm.CanSend("x", 2));

        vm.Mode = ImageEditMode.Multi;
        Assert.False(vm.CanSend("x", 1));
        Assert.True(vm.CanSend("x", 2));
        Assert.True(vm.CanSend("x", 0));
    }

    [Fact]
    public void AddHint_Appends_System_Error_Message()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));

        vm.AddHint("请先导入图片");

        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.IsError && m.Text == "请先导入图片");
    }

    [Fact]
    public void SetNodeMask_Forwards_To_Session_Without_Adding_A_Node()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;
        var mask = new MaskSpec { MaskImagePath = @"C:\img\mask.png", Width = 32, Height = 32 };

        vm.SetNodeMask(rootId, mask);

        Assert.Single(vm.History);
        Assert.Same(mask, session.Nodes[rootId].Mask);
        Assert.Same(mask, vm.History[0].Node.Mask);
        Assert.Same(mask, session.GetCurrentMaskSpec());
    }

    [Fact]
    public void SetNodeCrop_That_Clears_Mask_Adds_Hint()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;
        vm.SetNodeMask(rootId, new MaskSpec { MaskImagePath = @"C:\img\mask.png", Width = 32, Height = 32 });

        vm.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" });

        Assert.Null(session.Nodes[rootId].Mask);
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.Text == "裁切已改，遮罩已重置");
    }

    [Fact]
    public void SetNodeCrop_Without_Mask_Adds_No_Reset_Hint()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        vm.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" });

        Assert.DoesNotContain(vm.Messages, m => m.Text == "裁切已改，遮罩已重置");
    }
}
