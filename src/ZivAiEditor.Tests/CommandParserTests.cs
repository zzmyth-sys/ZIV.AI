using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Imaging;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 8 command parser tests (no LLM, no GPU).</summary>
public class CommandParserTests
{
    private const string SourceImage = @"C:\img\source.png";

    private static EditSession SessionWithImage()
    {
        var session = new EditSession();
        session.SetRoot(SourceImage);
        return session;
    }

    /// <summary>A path that does not exist, so the parser uses its built-in command set.</summary>
    private static CommandParser ParserWithoutFile()
        => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"));

    [Fact]
    public async Task SlashCommand_Matches_And_Parses()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/去水印", SessionWithImage());

        Assert.True(result.Success);
        Assert.Equal("/去水印", result.MatchedCommand);
        Assert.NotNull(result.Plan);
        Assert.Equal(SourceImage, result.Plan!.MainImagePath);

        var step = Assert.Single(result.Plan.Steps);
        Assert.Equal("QW21edit", step.ToolName);
        Assert.False(string.IsNullOrWhiteSpace(step.Parameters["prompt"]));
    }

    [Fact]
    public async Task SlashCommand_WithParams_Replaces_Template()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换背景 热带海滩", SessionWithImage());

        Assert.True(result.Success);
        var step = Assert.Single(result.Plan!.Steps);
        var prompt = step.Parameters["prompt"];
        Assert.Contains("热带海滩", prompt);
        Assert.DoesNotContain("{target}", prompt);
    }

    [Fact]
    public async Task SlashCommand_Unknown_Returns_Error()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/不存在的命令", SessionWithImage());

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.Null(result.MatchedCommand);
        Assert.Contains("Unknown command", result.ErrorMessage);
    }

    [Fact]
    public async Task SlashCommand_WrongParamCount_Returns_Error()
    {
        var parser = ParserWithoutFile();

        var missing = await parser.ParseAsync("/换背景", SessionWithImage());
        var extra = await parser.ParseAsync("/去水印 多余参数", SessionWithImage());

        Assert.False(missing.Success);
        Assert.NotNull(missing.ErrorMessage);
        Assert.False(extra.Success);
        Assert.NotNull(extra.ErrorMessage);
    }

    [Fact]
    public async Task SlashCommand_Outpaint_Sets_Explicit_Resolution()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/扩图 2048 1280", SessionWithImage());

        Assert.True(result.Success);
        var step = Assert.Single(result.Plan!.Steps);
        Assert.Equal("QW21outpaint", step.ToolName);
        Assert.NotNull(result.Plan.Resolution);
        Assert.Equal(ResolutionMode.Explicit, result.Plan.Resolution!.Mode);
        Assert.Equal(2048, result.Plan.Resolution.Width);
        Assert.Equal(1280, result.Plan.Resolution.Height);
    }

    [Fact]
    public async Task NaturalLanguage_Becomes_SingleStep_Plan()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("把天空换成日落", SessionWithImage());

        Assert.True(result.Success);
        Assert.Null(result.MatchedCommand);
        var step = Assert.Single(result.Plan!.Steps);
        Assert.Equal("QW21edit", step.ToolName);
        Assert.Equal("把天空换成日落", step.Parameters["prompt"]);
        Assert.Equal(SourceImage, result.Plan.MainImagePath);
    }

    [Fact]
    public async Task NaturalLanguage_WithoutImage_Still_Builds_T2I_Plan()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("一只窗台上的猫", new EditSession());

        Assert.True(result.Success);
        Assert.Equal("", result.Plan!.MainImagePath);
        Assert.Single(result.Plan.Steps);
    }

    [Fact]
    public async Task NoImage_NoPrompt_Returns_Error()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("   ", new EditSession());

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ResolutionOverload_Stamps_Resolution_When_Plan_Has_None()
    {
        var parser = ParserWithoutFile();
        var resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 };

        var result = await parser.ParseAsync("把天空换成日落", SessionWithImage(), resolution);

        Assert.True(result.Success);
        Assert.NotNull(result.Plan!.Resolution);
        Assert.Equal(ResolutionMode.Side, result.Plan.Resolution!.Mode);
        Assert.Equal(1536, result.Plan.Resolution.Side);
    }

    [Fact]
    public async Task ResolutionOverload_Keeps_Parser_Resolution()
    {
        var parser = ParserWithoutFile();
        var injected = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 };

        // /扩图 carries its own explicit resolution, which must win over the injected one.
        var result = await parser.ParseAsync("/扩图 2048 1280", SessionWithImage(), injected);

        Assert.True(result.Success);
        Assert.Equal(ResolutionMode.Explicit, result.Plan!.Resolution!.Mode);
        Assert.Equal(2048, result.Plan.Resolution.Width);
        Assert.Equal(1280, result.Plan.Resolution.Height);
    }

    [Fact]
    public async Task ResolutionOverload_Null_Leaves_Plan_Unchanged()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("把天空换成日落", SessionWithImage(), resolution: null);

        Assert.True(result.Success);
        Assert.Null(result.Plan!.Resolution);
    }

    [Fact]
    public async Task Old_Overload_Leaves_Resolution_Null()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("把天空换成日落", SessionWithImage());

        Assert.True(result.Success);
        Assert.Null(result.Plan!.Resolution);
    }

    [Fact]
    public async Task CommandsJson_Missing_Uses_Default()
    {
        var parser = ParserWithoutFile();

        Assert.Contains(parser.Commands, command => command.Name == "/换背景");
        Assert.Contains(parser.Commands, command => command.Name == "/去水印");

        var result = await parser.ParseAsync("/去水印", SessionWithImage());
        Assert.True(result.Success);
    }

    [Fact]
    public async Task CommandsJson_File_Is_Loaded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zivai_cmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "commands.json");
        await File.WriteAllTextAsync(path, """
            {
              "version": "1.0",
              "commands": [
                {
                  "name": "/测试",
                  "params": ["value"],
                  "tool": "QW21edit",
                  "template": "Set the style to {value}.",
                  "description": "测试命令"
                }
              ]
            }
            """);

        try
        {
            var parser = new CommandParser(path);

            Assert.Single(parser.Commands);
            var result = await parser.ParseAsync("/测试 水墨", SessionWithImage());
            Assert.True(result.Success);
            Assert.Equal("/测试", result.MatchedCommand);
            Assert.Equal("Set the style to 水墨.", result.Plan!.Steps[0].Parameters["prompt"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Plan_Carries_Current_Node_Mask()
    {
        var parser = ParserWithoutFile();
        var session = SessionWithImage();
        var rootId = session.CurrentNodeId!;
        session.SetNodeMask(rootId, new MaskSpec
        {
            MaskImagePath = @"C:\img\mask.png",
            Width = 32,
            Height = 32,
            IsBinary = true,
        });

        var slash = await parser.ParseAsync("/去水印", session);
        var natural = await parser.ParseAsync("把天空换成日落", session);

        Assert.NotNull(slash.Plan!.Mask);
        Assert.Equal(@"C:\img\mask.png", slash.Plan.Mask!.MaskImagePath);
        Assert.NotNull(natural.Plan!.Mask);
        Assert.Equal(@"C:\img\mask.png", natural.Plan.Mask!.MaskImagePath);
    }

    [Fact]
    public async Task Plan_Has_No_Mask_When_Node_Unmasked()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/去水印", SessionWithImage());

        Assert.True(result.Success);
        Assert.Null(result.Plan!.Mask);
    }
}
