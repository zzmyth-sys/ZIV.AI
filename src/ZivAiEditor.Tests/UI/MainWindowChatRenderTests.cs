using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
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

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// B1: the chat stream is rebuilt by a single coalesced (dispatcher-deferred) render per batch of
/// <c>Messages</c> changes, instead of one full rebuild per change (the former O(n^2) path with a
/// per-change bitmap dispose / re-decode). Headless only — no Python / GPU / real window.
/// </summary>
public class MainWindowChatRenderTests
{
    private static MainWindow BuildWindow(EditSession session)
    {
        var parser = new CommandParser(new List<CommandDefinition>());
        var executor = new Executor(new ToolRegistry(), new ExecutionQueue(), session, session, parser);
        var store = new SessionStore(Path.Combine(
            Path.GetTempPath(), "zivai-chatrender-" + Guid.NewGuid().ToString("N")));
        var projects = new ProjectService(store);
        var imaging = new ImagingService();
        var modelProfiles = new ModelProfileRegistry(modelsFilePath: null);
        var shell = new ShellService();

        return new MainWindow(
            session, session, parser, executor, store, projects, imaging, shell, modelProfiles);
    }

    private static EditSession SessionWithNodes(int childCount)
    {
        var session = new EditSession();
        // Image paths deliberately do not exist: AddPreview degrades to a "[预览失败]" text bubble,
        // so each message still yields exactly one stream child (render-count behaviour is what
        // we assert, not decoding).
        session.SetRoot("missing-root.png");
        var parent = session.CurrentNodeId;
        for (var i = 0; i < childCount; i++)
        {
            var node = session.AppendNode(parent, $"missing-{i}.png", $"/step{i}");
            parent = node.NodeId;
        }

        return session;
    }

    [Fact]
    public void ChatStream_Children_Match_Messages_After_Coalesced_Render()
    {
        var session = SessionWithNodes(childCount: 4);

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var stream = window.FindControl<StackPanel>("PART_ChatStream");
                Assert.NotNull(stream);
                Assert.True(window.ViewModel.Messages.Count > 0);
                Assert.Equal(window.ViewModel.Messages.Count, stream!.Children.Count);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void One_RebuildContext_Triggers_One_Render()
    {
        // 13-node path -> 25 messages without coalescing (RebuildContext would be O(n^2)).
        var session = SessionWithNodes(childCount: 12);

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var before = window.ChatRenderCount;
                window.ViewModel.RebuildContext();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, window.ChatRenderCount - before);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void Two_Independent_Changes_Render_Twice()
    {
        var session = SessionWithNodes(childCount: 0);

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = BuildWindow(session);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var before = window.ChatRenderCount;
                window.ViewModel.AddHint("a");
                Dispatcher.UIThread.RunJobs();
                window.ViewModel.AddHint("b");
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(2, window.ChatRenderCount - before);
            }
            finally
            {
                window?.Close();
            }
        });
    }
}
