using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Xunit;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.App;
using ZivAiEditor.App.Controls;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.Imaging;
using ZivAiEditor.Tools;
using ZivAiEditor.UI;

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// Headless tests for the command-level LoRA wiring in <see cref="MainWindow"/> and the pure
/// write-back helper <see cref="LoraCommandEdit"/>. No Python / GPU / real window; the modal
/// save dialog is intentionally not exercised here (covered separately end-to-end via the
/// template store, which has no UI).
/// </summary>
public class MainWindowLoraTests
{
    [Fact]
    public void LoraControl_Visible_Only_For_Lora_Command()
    {
        var session = new EditSession();
        var commands = Commands();
        var parser = new CommandParser(commands);
        var executor = new Executor(new ToolRegistry(), new ExecutionQueue(), session, session, parser);
        var store = new SessionStore(Path.Combine(
            Path.GetTempPath(), "zivai-lora-mw-" + Guid.NewGuid().ToString("N")));
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
                    session, session, parser, executor, store, projects, imaging, shell,
                    modelProfiles, commands: commands);
                window.Show();

                var input = window.FindControl<TextBox>("PART_Input");
                var lora = window.FindControl<LoraControl>("PART_LoraControl");
                Assert.NotNull(input);
                Assert.NotNull(lora);

                input!.Text = "/换装";
                Dispatcher.UIThread.RunJobs();
                Assert.False(lora!.IsVisible);

                input.Text = "/换脸";
                Dispatcher.UIThread.RunJobs();
                Assert.True(lora.IsVisible);

                input.Text = "hello";
                Dispatcher.UIThread.RunJobs();
                Assert.False(lora.IsVisible);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void CommandsReloaded_Refreshes_MainWindow_Command_Snapshot()
    {
        // Z-030 复议 / P2: AppContext.ReloadCommands() swaps the parser in place and raises
        // CommandsReloaded; the window mirrors the new snapshot — a user-authored LoRA on a
        // non-capability command makes its panel appear.
        var directory = Path.Combine(Path.GetTempPath(), "zivai-lora-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            WriteCommands(directory, loraEnabled: true, extraName: "/旧命令");
            using var context = ZivAiEditor.App.AppContext.Create(Shell(directory));

            HeadlessTest.Run(() =>
            {
                MainWindow? window = null;
                try
                {
                    window = NewWindow(context);
                    window.Show();

                    var input = window.FindControl<TextBox>("PART_Input")!;
                    var lora = window.FindControl<LoraControl>("PART_LoraControl")!;

                    input.Text = "/旧命令";
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(lora.IsVisible);

                    // Author a LoRA on the (non-capability) command via the user override, then
                    // hot-reload: the merged snapshot changes, so the panel appears.
                    WriteUserCommands(directory, "/旧命令", loraEnabled: true);
                    context.ReloadCommands();
                    Dispatcher.UIThread.RunJobs();

                    Assert.True(lora.IsVisible);
                }
                finally
                {
                    window?.Close();
                }
            });
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void Lora_Panel_Persists_After_Disable()
    {
        // Z-030 P2: the built-in /换脸 keeps its LoRA (capability), so turning it off in the user
        // override hides nothing — the panel stays and the control shows the off state.
        var directory = Path.Combine(Path.GetTempPath(), "zivai-lora-off-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            WriteCommands(directory, loraEnabled: true, extraName: "/旧命令");
            using var context = ZivAiEditor.App.AppContext.Create(Shell(directory));

            HeadlessTest.Run(() =>
            {
                MainWindow? window = null;
                try
                {
                    window = NewWindow(context);
                    window.Show();
                    var input = window.FindControl<TextBox>("PART_Input")!;
                    var lora = window.FindControl<LoraControl>("PART_LoraControl")!;

                    input.Text = "/换脸";
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(lora.IsVisible);
                    Assert.True(lora.CurrentState.Enabled);

                    // Persist OFF to the user override; the built-in file still declares the LoRA.
                    WriteUserCommands(directory, "/换脸", loraEnabled: false);
                    context.ReloadCommands();
                    Dispatcher.UIThread.RunJobs();

                    Assert.True(lora.IsVisible);
                    Assert.False(lora.CurrentState.Enabled);
                }
                finally
                {
                    window?.Close();
                }
            });
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static FakeShell Shell(string directory)
        => new(directory, new BackendSettings
        {
            Prewarm = false,
            PythonExe = "python.exe",
            Script = Path.Combine(directory, "main.py"),
        });

    private static MainWindow NewWindow(ZivAiEditor.App.AppContext context)
        => new(
            context.Session, context.SessionWriter, context.CommandParser, context.Executor,
            context.SessionStore, context.Projects, context.Imaging, new ShellService(),
            context.ModelProfiles, commands: context.Commands,
            commandTemplates: context.CommandTemplates, appContext: context);

    private static void WriteUserCommands(string directory, string name, bool loraEnabled)
    {
        var lora = loraEnabled
            ? "\"loras\": [ { \"path\": \"face-swap\", \"strength_model\": 1.0, \"strength_clip\": 1.0 } ], "
            : "";
        var json = $$"""
            {
              "version": "1.0",
              "commands": [
                { "name": "{{name}}", "params": ["description"], "variadic": true, "tool": "QW21edit",
                  {{lora}}"template": "tpl {description}" }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(directory, "commands.user.json"), json);
    }

    private static void WriteCommands(string directory, bool loraEnabled, string extraName)
    {
        var lora = loraEnabled
            ? "\"loras\": [ { \"path\": \"face-swap\", \"strength_model\": 1.0, \"strength_clip\": 1.0 } ], "
            : "";
        var json = $$"""
            {
              "version": "1.0",
              "commands": [
                { "name": "/换脸", "params": ["description"], "variadic": true, "tool": "QW21edit",
                  {{lora}}"template": "swap {description}" },
                { "name": "{{extraName}}", "tool": "QW21edit", "template": "x" }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(directory, "commands.json"), json);
    }

    private static List<CommandDefinition> Commands() => new()
    {
        new CommandDefinition
        {
            Name = "/换脸",
            Tool = "QW21edit",
            Template = "tpl",
            Variadic = true,
            Loras = new List<LoraOptions>
            {
                new LoraOptions { Path = "face-swap", StrengthModel = 1.0, StrengthClip = 1.0 },
            },
        },
        new CommandDefinition { Name = "/换装", Tool = "QW21edit", Template = "tpl", Variadic = true },
    };

    private sealed class FakeShell : IShellContext
    {
        private readonly BackendSettings _settings;

        public FakeShell(string templateDirectory, BackendSettings settings)
        {
            TemplateDirectory = templateDirectory;
            _settings = settings;
        }

        public BackendSettings LoadSettings() => _settings;

        public string TemplateDirectory { get; }

        public event Action<LaunchOptions>? LaunchRequested
        {
            add { }
            remove { }
        }

        public void OpenFolder(string path)
        {
        }

        public Task<string?> PickFolderAsync(
            Window owner,
            string title,
            string? suggestedDirectory = null,
            CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(
            Window owner,
            string title,
            string? suggestedDirectory = null,
            CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}

/// <summary>Pure tests for the full-field clone / read-back match (no UI).</summary>
public class LoraCommandEditTests
{
    private static CommandDefinition Command() => new()
    {
        Name = "/换脸",
        Params = new List<string> { "description" },
        Tool = "QW21edit",
        Template = "tpl",
        Variants = new Dictionary<string, string>(StringComparer.Ordinal) { ["multi"] = "tpl" },
        DefaultVariant = "multi",
        Variadic = true,
        AllowEmptyPrompt = true,
        Description = "desc",
        FixedResolution = new ResolutionPolicy { Mode = ResolutionMode.Explicit, Width = 8, Height = 8 },
        Quick = true,
        ShortcutLabel = "换脸",
    };

    [Fact]
    public void Build_On_Adds_Single_Lora_And_Preserves_Every_Field()
    {
        var result = LoraCommandEdit.Build(Command(), new LoraUiState { Enabled = true, Strength = 1.5, Path = "face-swap" });

        var lora = Assert.Single(result.EffectiveLoras);
        Assert.Equal("face-swap", lora.Path);
        Assert.Equal(1.5, lora.StrengthModel!.Value);
        Assert.Equal(1.5, lora.StrengthClip!.Value);

        Assert.True(result.AllowEmptyPrompt);
        Assert.Equal("multi", result.DefaultVariant);
        Assert.Equal("tpl", result.Variants["multi"]);
        Assert.NotNull(result.FixedResolution);
        Assert.True(result.Quick);
        Assert.Equal("换脸", result.ShortcutLabel);
        Assert.Equal(new[] { "description" }, result.Params);
    }

    [Fact]
    public void Build_Off_Removes_The_Lora_But_Keeps_Fields()
    {
        var result = LoraCommandEdit.Build(Command(), new LoraUiState { Enabled = false });

        Assert.Empty(result.EffectiveLoras);
        Assert.True(result.AllowEmptyPrompt);
        Assert.Equal("multi", result.DefaultVariant);
    }

    [Fact]
    public void Matches_Respects_Enabled_Path_And_Strength()
    {
        var built = LoraCommandEdit.Build(Command(), new LoraUiState { Enabled = true, Strength = 0.8, Path = "p" });

        Assert.True(LoraCommandEdit.Matches(built, new LoraUiState { Enabled = true, Strength = 0.8, Path = "p" }));
        Assert.False(LoraCommandEdit.Matches(built, new LoraUiState { Enabled = true, Strength = 0.9, Path = "p" }));
        Assert.False(LoraCommandEdit.Matches(built, new LoraUiState { Enabled = true, Strength = 0.8, Path = "q" }));
        Assert.False(LoraCommandEdit.Matches(built, new LoraUiState { Enabled = false }));
        Assert.False(LoraCommandEdit.Matches(null, new LoraUiState { Enabled = false }));
    }

    [Fact]
    public void Save_RoundTrips_Through_The_Template_Store_As_A_User_Entry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zivai_lora_rt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new CommandTemplateService(directory);
            var current = service.List().Single(entry => entry.Definition.Name == "/换脸").Definition;
            var state = new LoraUiState { Enabled = true, Strength = 0.5, Path = "face-swap" };

            service.Update("/换脸", LoraCommandEdit.Build(current, state));

            var entry = service.List().Single(e => e.Definition.Name == "/换脸");
            Assert.Equal(CommandSource.User, entry.Source);
            Assert.True(LoraCommandEdit.Matches(entry.Definition, state));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
