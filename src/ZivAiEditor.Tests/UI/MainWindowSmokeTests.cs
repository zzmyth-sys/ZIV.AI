using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
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
/// Z-008 groundwork: proves a <see cref="MainWindow"/> can be constructed, bound and
/// closed under the Avalonia headless platform without starting Python, the GPU or a
/// real OS window. It is intentionally a smoke test (one case) so future MainWindow
/// decomposition has a regression net; it does not exercise behaviour.
/// </summary>
public class MainWindowSmokeTests
{
    [Fact]
    public void Constructs_HasKeyControls_And_Closes()
    {
        var session = new EditSession();
        var parser = new CommandParser(new List<CommandDefinition>());
        var executor = new Executor(new ToolRegistry(), new ExecutionQueue(), session, session, parser);
        var store = new SessionStore(Path.Combine(
            Path.GetTempPath(), "zivai-mainwindow-smoke-" + Guid.NewGuid().ToString("N")));
        var projects = new ProjectService(store);
        var imaging = new ImagingService();
        var modelProfiles = new ModelProfileRegistry(modelsFilePath: null);
        var shell = new ShellService();

        HeadlessTest.Run(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(
                    session, session, parser, executor, store, projects, imaging, shell, modelProfiles);

                window.Show();

                Assert.NotNull(window.FindControl<TextBox>("PART_Input"));
                Assert.NotNull(window.FindControl<ScrollViewer>("PART_ChatScroll"));
                Assert.NotNull(window.FindControl<ListBox>("PART_HistoryList"));
            }
            finally
            {
                window?.Close();
            }
        });
    }
}
