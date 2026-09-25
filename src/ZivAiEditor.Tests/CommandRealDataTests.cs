using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// P1b: the real <c>Template/commands.json</c> data file and the <c>BuiltInCommands</c>
/// fallback must agree. Covers <c>/全景</c> (Edit + fixed 2:1 resolution) and the
/// <c>/扩图</c> relocation (Edit + QW21edit, requiring a crop-tool outpaint). Pure parser;
/// no LLM / GPU.
/// </summary>
public class CommandRealDataTests
{
    private static EditSession SessionWithImage()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\source.png");
        return session;
    }

    private static CommandParser FromDataFile() => new(FindCommandsFile()!);

    private static CommandParser FromBuiltIn()
        => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"));

    /// <summary>Walks up from the test bin directory to the repository root's commands file.</summary>
    private static string? FindCommandsFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Template", "commands.json");
            if (File.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "DOC", "FROZEN.md")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    [Fact]
    public async Task Panorama_NoArgs_Uses_Fixed_Explicit_Resolution()
    {
        var parser = FromDataFile();
        Assert.NotNull(FindCommandsFile());

        var command = Assert.Single(parser.Commands, candidate => candidate.Name == "/全景");
        Assert.Equal(CommandHandler.Edit, command.Handler);
        Assert.Equal("QW21edit", command.Tool);
        Assert.Empty(command.Params);

        var result = await parser.ParseAsync("/全景", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        Assert.Equal("/全景", result.MatchedCommand);
        Assert.Equal("QW21edit", Assert.Single(result.Plan!.Steps).ToolName);
        var resolution = Assert.IsType<ResolutionPolicy>(result.Plan.Resolution);
        Assert.Equal(ResolutionMode.Explicit, resolution.Mode);
        Assert.Equal(2048, resolution.Width);
        Assert.Equal(1024, resolution.Height);
    }

    [Fact]
    public async Task Panorama_Fixed_Resolution_Wins_Over_Injected_Tier()
    {
        var parser = FromDataFile();
        var injected = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 };

        var result = await parser.ParseAsync("/全景", SessionWithImage(), 1, injected);

        Assert.True(result.Success);
        var resolution = Assert.IsType<ResolutionPolicy>(result.Plan!.Resolution);
        Assert.Equal(ResolutionMode.Explicit, resolution.Mode);
        Assert.Equal(2048, resolution.Width);
        Assert.Equal(1024, resolution.Height);
    }

    [Fact]
    public async Task Kuotu_Handler_Edit_Needs_Outpaint_Crop()
    {
        var parser = FromDataFile();

        var command = Assert.Single(parser.Commands, candidate => candidate.Name == "/扩图");
        Assert.Equal(CommandHandler.Edit, command.EffectiveHandler);
        Assert.Equal("QW21edit", command.Tool);
        Assert.Empty(command.Params);

        // P1: with no outpaint crop on the current node, the name-based gate rejects it.
        var result = await parser.ParseAsync("/扩图", SessionWithImage(), 1, resolution: null);
        Assert.False(result.Success);
        Assert.Contains("需先做裁切外扩", result.ErrorMessage);
    }

    [Fact]
    public void BuiltIn_Matches_DataFile()
    {
        var data = FromDataFile();
        var builtIn = FromBuiltIn();

        Assert.Equal(
            data.Commands.Select(command => command.Name).OrderBy(name => name, StringComparer.Ordinal),
            builtIn.Commands.Select(command => command.Name).OrderBy(name => name, StringComparer.Ordinal));

        var panorama = builtIn.Commands.Single(command => command.Name == "/全景");
        Assert.Equal(CommandHandler.Edit, panorama.Handler);
        Assert.Equal("QW21edit", panorama.Tool);
        Assert.Equal(2048, panorama.FixedResolution!.Width);
        Assert.Equal(1024, panorama.FixedResolution.Height);

        Assert.Equal(
            CommandHandler.Edit,
            builtIn.Commands.Single(command => command.Name == "/扩图").EffectiveHandler);
    }
}
