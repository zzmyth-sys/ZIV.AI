using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.UI;
using ZivAiEditor.UI.Chat;
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

    [Fact]
    public async Task Submit_Appends_Node_And_Refreshes_History()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });

        var ok = await vm.SubmitAsync("/去水印");

        Assert.True(ok);
        Assert.Single(vm.History);
        Assert.Equal(Output, session.GetCurrentImagePath());
        Assert.Contains(vm.Messages, m => m.Role == ChatRole.User && m.Text == "/去水印");
        Assert.Contains(vm.Messages, m => m.ImagePath == Output);
    }

    [Fact]
    public async Task Submit_Failure_Shows_Error_And_Adds_No_Node()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, ParserWithoutFile(), new FakeExecutor(null, success: false));
        vm.Start(new LaunchOptions { ImagePath = Root });

        var ok = await vm.SubmitAsync("把天空换成日落");

        Assert.False(ok);
        Assert.Empty(vm.History);
        Assert.Contains(vm.Messages, m => m.IsError);
    }

    [Fact]
    public async Task Navigate_Switches_Current_And_Rebuilds_Chat()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, ParserWithoutFile(), new FakeExecutor(Output));
        vm.Start(new LaunchOptions { ImagePath = Root });
        await vm.SubmitAsync("/去水印");

        var nodeId = vm.History[0].Node.NodeId;

        Assert.True(vm.NavigateTo(nodeId));
        Assert.Equal(nodeId, session.CurrentNodeId);
        Assert.Contains(vm.Messages, m => m.Text == "/去水印");
        Assert.False(vm.NavigateTo("missing"));
    }

    [Fact]
    public void Empty_Session_Starts_Without_Messages()
    {
        var session = new EditSession();
        var vm = new SessionViewModel(session, ParserWithoutFile(), new FakeExecutor(Output));

        vm.Start(new LaunchOptions());

        Assert.Empty(vm.History);
        Assert.Empty(vm.Messages);
    }
}
