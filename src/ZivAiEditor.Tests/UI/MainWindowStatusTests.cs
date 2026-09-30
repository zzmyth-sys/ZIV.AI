using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using Xunit;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.App;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Imaging;
using ZivAiEditor.Tools;
using ZivAiEditor.UI.Chat;
using ChatMessage = ZivAiEditor.UI.Chat.ChatMessage;

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// B14: the in-flight "生成中" bubble's status text survives a chat rebuild, and a completed run
/// no longer writes status into a detached label. Headless only — no Python / GPU / real window.
/// </summary>
public class MainWindowStatusTests
{
    private static MainWindow BuildWindow(EditSession session)
    {
        var parser = new CommandParser(new List<CommandDefinition>());
        var executor = new Executor(new ToolRegistry(), new ExecutionQueue(), session, session, parser);
        var store = new SessionStore(Path.Combine(
            Path.GetTempPath(), "zivai-status-mw-" + Guid.NewGuid().ToString("N")));
        var projects = new ProjectService(store);
        var imaging = new ImagingService();
        var modelProfiles = new ModelProfileRegistry(modelsFilePath: null);
        var shell = new ShellService();

        return new MainWindow(
            session, session, parser, executor, store, projects, imaging, shell, modelProfiles);
    }

    private static void AddPendingBubble(MainWindow window)
    {
        window.ViewModel.Messages.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Text = "生成中…",
            IsPending = true,
        });
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void Progress_Text_Survives_A_Chat_Rebuild()
    {
        var session = new EditSession();

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AddPendingBubble(window);
                Assert.Equal("生成中…", window.PendingStatusText);

                window.SetStatusForTest("进度X");
                Assert.Equal("进度X", window.PendingStatusText);

                // A side rebuild (a hint appended) destroys + recreates the pending label.
                window.ViewModel.AddHint("旁路");
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("进度X", window.PendingStatusText);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void Completed_Bubble_Does_Not_Receive_Later_Status()
    {
        var session = new EditSession();

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AddPendingBubble(window);
                window.SetStatusForTest("进度X");

                // Complete the run: the pending bubble is replaced by a final (non-pending) one.
                var pending = window.ViewModel.Messages.First(m => m.IsPending);
                window.ViewModel.ReplacePending(pending, new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Text = "完成",
                });
                Dispatcher.UIThread.RunJobs();

                Assert.Null(window.PendingStatusText);

                // A later status must not be written into the removed control.
                window.SetStatusForTest("已保存");
                Assert.Null(window.PendingStatusText);
            }
            finally
            {
                window?.Close();
            }
        });
    }
}
