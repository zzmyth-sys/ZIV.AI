using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;
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
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => ExecuteAsync(new EditPlan(), progress, ct);

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
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => ExecuteAsync(new EditPlan(), progress, ct);

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
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => ExecuteAsync(new EditPlan(), progress, ct);

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
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => ExecuteAsync(new EditPlan(), progress, ct);

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>Returns a queued output per execute / rerun call, so submit + rerun differ.</summary>
    private sealed class SequenceExecutor : IExecutor
    {
        private readonly Queue<string> _outputs;

        public SequenceExecutor(params string[] outputs) => _outputs = new Queue<string>(outputs);

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => Next(plan);

        public Task<TaskState> RerunAsync(
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => Next(new EditPlan());

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);

        private Task<TaskState> Next(EditPlan plan)
            => Task.FromResult(new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = TaskStatus.Succeeded,
                Plan = plan,
                OutputImagePath = _outputs.Dequeue(),
            });
    }

    /// <summary>Submit succeeds, re-run fails (Step 9C.8-A3 failure-bubble test).</summary>
    private sealed class RerunFailExecutor : IExecutor
    {
        private readonly string _output;

        public RerunFailExecutor(string output) => _output = output;

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => Task.FromResult(new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = TaskStatus.Succeeded,
                Plan = plan,
                OutputImagePath = _output,
            });

        public Task<TaskState> RerunAsync(
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => Task.FromResult(new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = TaskStatus.Failed,
                ErrorMessage = "模拟重跑失败",
            });

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>Blocks until canceled; returns a Canceled state (Step 9C.8-B).</summary>
    private sealed class BlockingExecutor : IExecutor
    {
        private readonly string? _output;

        public BlockingExecutor(string? output) => _output = output;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => BlockAsync(plan, ct);

        public Task<TaskState> RerunAsync(
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => BlockAsync(new EditPlan(), ct);

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);

        private async Task<TaskState> BlockAsync(EditPlan plan, CancellationToken ct)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                return new TaskState
                {
                    TaskId = Guid.NewGuid().ToString("N"),
                    Status = TaskStatus.Canceled,
                    Plan = plan,
                    ErrorMessage = "canceled",
                };
            }

            return new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = TaskStatus.Succeeded,
                Plan = plan,
                OutputImagePath = _output,
            };
        }
    }

    /// <summary>Submit succeeds; re-run blocks until canceled (Step 9C.8-B).</summary>
    private sealed class BlockingRerunExecutor : IExecutor
    {
        private readonly string _submitOutput;

        public BlockingRerunExecutor(string submitOutput) => _submitOutput = submitOutput;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TaskState> ExecuteAsync(
            EditPlan plan,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
            => Task.FromResult(new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = TaskStatus.Succeeded,
                Plan = plan,
                OutputImagePath = _submitOutput,
            });

        public async Task<TaskState> RerunAsync(
            string nodeId,
            IProgress<TaskProgress>? progress = null,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                return new TaskState
                {
                    TaskId = Guid.NewGuid().ToString("N"),
                    Status = TaskStatus.Canceled,
                    ErrorMessage = "canceled",
                };
            }

            return new TaskState
            {
                TaskId = Guid.NewGuid().ToString("N"),
                Status = TaskStatus.Succeeded,
                OutputImagePath = @"C:\img\rerun.png",
            };
        }

        public Task<bool> CancelAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    [Fact]
    public async Task Submit_Retries_Once_After_Cuda_Oom()
    {
        var session = new EditSession();
        var executor = new OomOnceExecutor(Output);
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
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
    public async Task Submit_Records_UsedImages_Main_Then_References()
    {
        // Step 9C.10: the ordered pipeline images (main first) are stored on the node.
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        await vm.SubmitAsync(
            "用 <image2> 的风格",
            additionalImages: new[] { @"C:\img\r1.png", @"C:\img\r2.png" });

        var node = session.GetHistory().Single(n => !string.IsNullOrEmpty(n.ParentNodeId));
        Assert.Equal(new[] { Root, @"C:\img\r1.png", @"C:\img\r2.png" }, node.UsedImagePaths);
    }

    [Fact]
    public async Task Submit_Truncates_To_Three_References_And_Hints_After_User_Message()
    {
        var session = new EditSession();
        var executor = new CapturingExecutor(Output);
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });

        await vm.SubmitAsync("/去水印");

        Assert.NotNull(executor.LastPlan);
        Assert.Empty(executor.LastPlan!.AdditionalImages);
    }

    [Fact]
    public async Task Submit_Failure_Shows_Error_And_Adds_No_Node()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(null, success: false));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));

        vm.Start(new LaunchOptions());

        Assert.Empty(vm.History);
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public async Task Submit_Marks_Pending_Bubble_While_Executing()
    {
        var session = new EditSession();
        var executor = new DeferredExecutor();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");

        Assert.Equal(Root, vm.GetParentImagePath(Output));
    }

    [Fact]
    public async Task GetParentImagePath_Returns_Parent_Node_Output_For_Grandchild()
    {
        const string second = @"C:\img\out2.png";
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        vm.SetRootImage((string?)null);
        vm.SetRootImage("   ");

        Assert.Equal(Root, session.RootImagePath);
    }

    [Fact]
    public void SetRootImage_Pack_Promotes_All_And_Tracks_Count()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        vm.SetRootImage(new[] { "a", "b", "c" });

        Assert.Equal("a", session.RootImagePath);
        Assert.Equal(new[] { "a", "b", "c" }, session.GetHistory()[0].ImagePaths);
        Assert.Equal(3, vm.CurrentImageCount);
        Assert.Single(vm.History);
    }

    [Fact]
    public void SetRootImage_Pack_Trims_To_Ten_And_Adds_Info()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());
        var many = Enumerable.Range(0, 12).Select(i => $"img{i}").ToArray();

        vm.SetRootImage(many);

        Assert.Equal(10, session.GetHistory()[0].ImagePaths.Count);
        Assert.Equal(10, vm.CurrentImageCount);
        Assert.Equal("img0", session.RootImagePath);
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && !m.IsError);
    }

    [Fact]
    public void CurrentImageCount_Is_Zero_When_No_Node()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        Assert.Equal(0, vm.CurrentImageCount);
    }

    [Fact]
    public async Task Submit_With_Multi_Image_Root_Feeds_Pack_As_References()
    {
        // Step 9C.10-P2 (Q1=A): a multi-image root's whole pack is the pipeline; the main is
        // pack[0] and the extras become references (main first in UsedImagePaths).
        var session = new EditSession();
        var executor = new CapturingExecutor(Output);
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
        vm.SetRootImage(new[] { "a", "b", "c" });

        var ok = await vm.SubmitAsync("/去水印");

        Assert.True(ok);
        Assert.Equal("a", executor.LastPlan!.MainImagePath);
        Assert.Equal(new[] { "b", "c" }, executor.LastPlan.AdditionalImages);
        var node = session.GetHistory().Single(n => !string.IsNullOrEmpty(n.ParentNodeId));
        Assert.Equal(new[] { "a", "b", "c" }, node.UsedImagePaths);
        Assert.Contains(
            vm.Messages,
            m => m.Role == ChatRole.System && !m.IsError && m.Text == "本次使用 3 张图");
    }

    [Fact]
    public async Task Submit_Truncates_Pack_Plus_Attachments_To_Three_Refs()
    {
        // Pack extras (b,c,d) come before the attachments, and the combined refs cap at 3.
        var session = new EditSession();
        var executor = new CapturingExecutor(Output);
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
        vm.SetRootImage(new[] { "a", "b", "c", "d" });

        await vm.SubmitAsync("编辑", additionalImages: new[] { "r1", "r2" });

        Assert.Equal(new[] { "b", "c", "d" }, executor.LastPlan!.AdditionalImages);
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.IsError);
    }

    [Fact]
    public async Task Submit_Pure_T2I_Skips_The_Usage_Info_Line()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        await vm.SubmitAsync("/生成 一只猫");

        Assert.DoesNotContain(
            vm.Messages,
            m => m.Role == ChatRole.System && m.Text.StartsWith("本次使用", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Submit_T2I_With_MultiImage_Root_Does_Not_Use_The_Pack()
    {
        // A T2I plan has no main image, so the root pack must not be attached as references
        // (Step 9C.10): no phantom refs, no usage info line, no used images on the node.
        var session = new EditSession();
        var executor = new CapturingExecutor(Output);
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
        vm.SetRootImage(new[] { "a", "b", "c" });

        await vm.SubmitAsync("/生成 一只猫");

        Assert.NotNull(executor.LastPlan);
        Assert.True(string.IsNullOrWhiteSpace(executor.LastPlan!.MainImagePath));
        Assert.Empty(executor.LastPlan.AdditionalImages);
        var node = session.GetHistory().Single(n => !string.IsNullOrEmpty(n.ParentNodeId));
        Assert.Empty(node.UsedImagePaths);
        Assert.DoesNotContain(
            vm.Messages,
            m => m.Role == ChatRole.System && m.Text.StartsWith("本次使用", StringComparison.Ordinal));
    }

    [Fact]
    public void AddInfo_Appends_System_NonError_Message()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));

        vm.AddInfo("本次使用 2 张图");

        Assert.Contains(
            vm.Messages,
            m => m.Role == ChatRole.System && !m.IsError && m.Text == "本次使用 2 张图");
    }

    [Fact]
    public void RebuildContext_Starting_Bubble_Carries_The_Display_Pack()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.SetRootImage(new[] { "a", "b", "c" });

        var bubble = vm.Messages.Single(m => m.Role == ChatRole.System && m.Text == "起始图像");
        Assert.Equal("a", bubble.ImagePath);
        Assert.Equal(new[] { "a", "b", "c" }, bubble.ImagePaths);
    }

    [Fact]
    public void SetNodeCrop_Stores_Crop_Without_Adding_A_Node()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        vm.SetNodeCrop("missing", new CropSpec { Width = 10, Height = 10 });

        Assert.Single(vm.History);
        Assert.Null(vm.History[0].Node.Crop);
    }

    [Fact]
    public void SetNodeCrop_Readjust_Does_Not_Add_A_Node()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        vm.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = cropResult });

        // The chat's "起始图像" bubble now carries the crop result, not the original.
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.ImagePath == cropResult);
        Assert.DoesNotContain(vm.Messages, m => m.ImagePath == Root);
    }

    [Fact]
    public void SetNodeMask_Shows_Mask_On_The_Bubble_And_Clears_It()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        vm.SetNodeMask(rootId, new MaskSpec
        {
            MaskImagePath = @"C:\img\m.png",
            Width = 8,
            Height = 8,
            FeatherPx = 6,
        });

        var bubble = vm.Messages.Single(m => m.Role == ChatRole.System && m.ImagePath == Root);
        Assert.Equal(@"C:\img\m.png", bubble.MaskPath);
        Assert.Equal(6, bubble.MaskFeatherPx);

        vm.SetNodeMask(rootId, null);

        var cleared = vm.Messages.Single(m => m.Role == ChatRole.System && m.ImagePath == Root);
        Assert.Null(cleared.MaskPath);
        Assert.Equal(0, cleared.MaskFeatherPx);
    }

    [Fact]
    public void AlignForMask_Moves_Current_To_Previewed_Node()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;
        var child = session.AppendNode(rootId, @"C:\img\child.png", "child");
        vm.RefreshHistory();

        Assert.Equal(child.NodeId, session.CurrentNodeId);

        var moved = vm.AlignForMask(rootId);

        Assert.True(moved);
        Assert.Equal(rootId, session.CurrentNodeId);
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.Text.Contains("绘制遮罩"));
    }

    [Fact]
    public void AlignForMask_NoOp_When_Already_Current_Or_Unknown()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        Assert.False(vm.AlignForMask(rootId));
        Assert.False(vm.AlignForMask("missing"));
        Assert.False(vm.AlignForMask(null));
        Assert.Equal(rootId, session.CurrentNodeId);
    }

    [Fact]
    public void GetParentPipelineImagePath_Is_Null_For_Root()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        Assert.Null(vm.GetParentPipelineImagePath(Root));
    }

    [Fact]
    public void PrepareAttachments_NoAttachments_NoRoot_NaturalLanguage_Allows_T2I()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        Assert.Equal(AttachmentPreparation.Ready, vm.PrepareAttachments("一只猫", Array.Empty<string>()));
    }

    [Fact]
    public void PrepareAttachments_NoAttachments_NoRoot_SlashCommand_Needs_Image()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions());

        Assert.Equal(AttachmentPreparation.NoImage, vm.PrepareAttachments("/去水印", Array.Empty<string>()));
    }

    [Fact]
    public void PrepareAttachments_Attachments_NoRoot_Promotes_First_To_Root()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        var result = vm.PrepareAttachments("编辑", new[] { @"C:\img\new.png" });

        Assert.Equal(AttachmentPreparation.NeedsDecision, result);
        Assert.Equal(Root, session.RootImagePath);
    }

    [Fact]
    public void PrepareAttachments_NoAttachments_WithRoot_Is_Ready()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        Assert.Equal(AttachmentPreparation.Ready, vm.PrepareAttachments("编辑", Array.Empty<string>()));
    }

    [Fact]
    public void StartNewSessionFrom_Resets_Root_To_First_Attachment()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        vm.StartNewSessionFrom(new[] { @"C:\img\new.png", @"C:\img\other.png" });

        Assert.Equal(@"C:\img\new.png", session.RootImagePath);
        Assert.Single(vm.History);
    }

    [Fact]
    public void CanSend_Validation_Matrix()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));

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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));

        vm.AddHint("请先导入图片");

        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.IsError && m.Text == "请先导入图片");
    }

    [Fact]
    public void SetNodeMask_Forwards_To_Session_Without_Adding_A_Node()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
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
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        vm.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" });

        Assert.DoesNotContain(vm.Messages, m => m.Text == "裁切已改，遮罩已重置");
    }

    [Fact]
    public async Task Submit_Stores_Rerun_Snapshot_On_Node()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        vm.Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1024 };

        await vm.SubmitAsync("编辑", additionalImages: new[] { "r1", "r2" });

        var node = session.Nodes[session.CurrentNodeId!];
        Assert.NotNull(node.Rerun);
        Assert.Equal(1024, node.Rerun!.Resolution!.Side);
        Assert.Equal(new[] { "r1", "r2" }, node.Rerun.AdditionalImages);
    }

    [Fact]
    public async Task Submit_Without_Resolution_Or_Refs_Stores_No_Snapshot()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        await vm.SubmitAsync("/去水印");

        Assert.Null(session.Nodes[session.CurrentNodeId!].Rerun);
    }

    [Fact]
    public async Task RerunNode_Replaces_Node_In_Place()
    {
        const string rerunOutput = @"C:\img\rerun.png";
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(
            session, session, ParserWithoutFile(), new SequenceExecutor(Output, rerunOutput));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");
        var nodeId = session.CurrentNodeId!;

        var ok = await vm.RerunNodeAsync(nodeId);

        Assert.True(ok);
        Assert.Equal(2, vm.History.Count);                // root + the same node, no sibling
        Assert.True(session.Nodes.ContainsKey(nodeId));   // same identity
        Assert.Equal(rerunOutput, session.Nodes[nodeId].ImagePath);
        Assert.Equal(nodeId, session.CurrentNodeId);
    }

    [Fact]
    public async Task RerunNode_Keeps_Snapshot_On_Node()
    {
        const string rerunOutput = @"C:\img\rerun.png";
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(
            session, session, ParserWithoutFile(), new SequenceExecutor(Output, rerunOutput));
        vm.Start(new LaunchOptions { ImagePath = Root });
        vm.Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 2048 };
        await vm.SubmitAsync("/去水印");
        var nodeId = session.CurrentNodeId!;

        await vm.RerunNodeAsync(nodeId);

        Assert.NotNull(session.Nodes[nodeId].Rerun);
        Assert.Equal(2048, session.Nodes[nodeId].Rerun!.Resolution!.Side);
    }

    [Fact]
    public async Task RerunNode_Clears_Crop_And_Mask()
    {
        const string rerunOutput = @"C:\img\rerun.png";
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(
            session, session, ParserWithoutFile(), new SequenceExecutor(Output, rerunOutput));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");
        var nodeId = session.CurrentNodeId!;
        vm.SetNodeCrop(nodeId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" });
        vm.SetNodeMask(nodeId, new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 8, Height = 8 });

        await vm.RerunNodeAsync(nodeId);

        Assert.Null(session.Nodes[nodeId].Crop);
        Assert.Null(session.Nodes[nodeId].Mask);
    }

    [Fact]
    public async Task RerunNode_Cascade_Deletes_Descendants()
    {
        const string rerunOutput = @"C:\img\rerun.png";
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(
            session, session, ParserWithoutFile(), new SequenceExecutor(Output, rerunOutput));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");
        var nodeId = session.CurrentNodeId!;
        var child = session.AppendNode(nodeId, @"C:\img\child.png", "child");

        var ok = await vm.RerunNodeAsync(nodeId);

        Assert.True(ok);
        Assert.False(session.Nodes.ContainsKey(child.NodeId));
        Assert.True(session.Nodes.ContainsKey(nodeId));
        Assert.Equal(nodeId, session.CurrentNodeId);      // re-pointed out of the deleted subtree
        Assert.Equal(2, vm.History.Count);
    }

    [Fact]
    public async Task RerunNode_Deletes_Old_Output_File()
    {
        const string rerunOutput = @"C:\img\rerun.png";
        var oldOutput = Path.Combine(Path.GetTempPath(), "zivai_rerun_" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(oldOutput, new byte[] { 1, 2, 3 });

        try
        {
            var session = new EditSession();
            var vm = FlowRunnerHarness.Create(
                session, session, ParserWithoutFile(), new SequenceExecutor(oldOutput, rerunOutput));
            vm.Start(new LaunchOptions { ImagePath = Root });
            await vm.SubmitAsync("/去水印");
            var nodeId = session.CurrentNodeId!;
            Assert.True(File.Exists(oldOutput));

            await vm.RerunNodeAsync(nodeId);

            Assert.False(File.Exists(oldOutput));
        }
        finally
        {
            if (File.Exists(oldOutput))
            {
                File.Delete(oldOutput);
            }
        }
    }

    [Fact]
    public async Task RerunNode_Unknown_Node_Is_Rejected()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        Assert.False(await vm.RerunNodeAsync("missing"));
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.IsError);
    }

    [Fact]
    public async Task RerunNode_Root_Is_Rejected_With_Hint()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        Assert.False(await vm.RerunNodeAsync(rootId));
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.System && m.IsError);
    }

    [Fact]
    public async Task Submit_Sets_NodeId_On_Assistant_Bubble()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        await vm.SubmitAsync("/去水印");

        var bubble = vm.Messages.Single(m => m.Role == ChatRole.Assistant);
        Assert.Equal(session.CurrentNodeId, bubble.NodeId);
    }

    [Fact]
    public async Task RerunNode_Adds_No_New_Message_And_Updates_Bubble_In_Place()
    {
        const string rerunOutput = @"C:\img\rerun.png";
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(
            session, session, ParserWithoutFile(), new SequenceExecutor(Output, rerunOutput));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");
        var nodeId = session.CurrentNodeId!;
        var countBefore = vm.Messages.Count;

        var ok = await vm.RerunNodeAsync(nodeId);

        Assert.True(ok);
        Assert.Equal(countBefore, vm.Messages.Count);   // no new bubble
        Assert.DoesNotContain(
            vm.Messages,
            m => m.Role == ChatRole.User && m.Text.StartsWith("重跑", StringComparison.Ordinal));
        var bubble = vm.Messages.Single(m => m.Role == ChatRole.Assistant && m.NodeId == nodeId);
        Assert.Equal(rerunOutput, bubble.ImagePath);
        Assert.False(bubble.IsPending);
    }

    [Fact]
    public async Task RerunNode_Failure_Shows_Error_On_Original_Bubble()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new RerunFailExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");
        var nodeId = session.CurrentNodeId!;
        var countBefore = vm.Messages.Count;

        var ok = await vm.RerunNodeAsync(nodeId);

        Assert.False(ok);
        Assert.Equal(countBefore, vm.Messages.Count);   // no new bubble
        var bubble = vm.Messages.Single(m => m.Role == ChatRole.Assistant && m.NodeId == nodeId);
        Assert.True(bubble.IsError);
        Assert.Equal(Output, bubble.ImagePath);         // old image kept on failure
        Assert.False(bubble.IsPending);
    }

    [Fact]
    public async Task Submit_DisplayText_Shows_Original_But_Stores_Expanded()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        const string expanded = "/生成 一只发光的森林精灵";

        var ok = await vm.SubmitAsync(expanded, displayText: "原始");

        Assert.True(ok);
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.User && m.Text == "原始");
        Assert.DoesNotContain(vm.Messages, m => m.Role == ChatRole.User && m.Text == expanded);
        Assert.Equal(expanded, session.Nodes[session.CurrentNodeId!].Command);
    }

    [Fact]
    public void CancelCurrent_With_No_InFlight_Returns_False()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));

        Assert.False(vm.CancelCurrent());
    }

    [Fact]
    public async Task Cancel_Submit_Reverts_Chat_And_Flags_Canceled()
    {
        var session = new EditSession();
        var executor = new BlockingExecutor(Output);
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });

        var submit = vm.SubmitAsync("/去水印");
        await executor.Started.Task;

        Assert.True(vm.CancelCurrent());     // first click cancels
        Assert.False(vm.CancelCurrent());    // second click is a no-op while in flight
        var ok = await submit;

        Assert.False(ok);
        Assert.True(vm.LastRunCanceled);
        Assert.Single(vm.History);           // only the root node, no new node
        Assert.False(vm.IsBusy);
        // The chat reverts to the pre-send state: no user bubble, no pending, no "已取消" bubble.
        Assert.DoesNotContain(vm.Messages, m => m.Role == ChatRole.User && m.Text == "/去水印");
        Assert.DoesNotContain(vm.Messages, m => m.IsPending);
        Assert.DoesNotContain(vm.Messages, m => m.Text == "已取消。");
        Assert.False(vm.CancelCurrent());    // nothing in flight after completion
    }

    [Fact]
    public async Task CanRerun_True_For_Child_False_For_Root_And_Unknown()
    {
        var session = new EditSession();
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        var rootId = session.CurrentNodeId!;

        Assert.False(vm.CanRerun(rootId));       // root has no source image
        Assert.False(vm.CanRerun("missing"));

        await vm.SubmitAsync("/去水印");

        Assert.True(vm.CanRerun(session.CurrentNodeId!));
    }

    [Fact]
    public async Task Cancel_Rerun_Keeps_Old_Image_And_Marks_Bubble_Canceled()
    {
        var session = new EditSession();
        var executor = new BlockingRerunExecutor(Output);
        var vm = FlowRunnerHarness.Create(session, session, ParserWithoutFile(), executor);
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");
        var nodeId = session.CurrentNodeId!;
        var countBefore = vm.Messages.Count;

        var rerun = vm.RerunNodeAsync(nodeId);
        await executor.Started.Task;
        Assert.True(vm.CancelCurrent());
        var ok = await rerun;

        Assert.False(ok);
        Assert.Equal(countBefore, vm.Messages.Count);        // no new bubble
        Assert.Equal(Output, session.Nodes[nodeId].ImagePath); // node untouched
        var bubble = vm.Messages.Single(m => m.Role == ChatRole.Assistant && m.NodeId == nodeId);
        Assert.True(bubble.IsError);
        Assert.Equal(Output, bubble.ImagePath);              // old image kept
        Assert.False(vm.IsBusy);
    }
}
