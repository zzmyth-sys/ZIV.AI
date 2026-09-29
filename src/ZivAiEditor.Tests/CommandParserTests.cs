using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Execution;
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

        var missing = await parser.ParseAsync("/去物体", SessionWithImage());
        var extra = await parser.ParseAsync("/去水印 多余参数", SessionWithImage());

        Assert.False(missing.Success);
        Assert.NotNull(missing.ErrorMessage);
        Assert.False(extra.Success);
        Assert.NotNull(extra.ErrorMessage);
    }

    [Fact]
    public async Task SlashCommand_Outpaint_Now_ZeroArgs_Requires_Outpaint_Crop()
    {
        var parser = ParserWithoutFile();

        // P1: /扩图 lost its width/height params and now needs the current node's outpaint crop.
        var withArgs = await parser.ParseAsync("/扩图 2048 1280", SessionWithImage());
        Assert.False(withArgs.Success);
        Assert.NotNull(withArgs.ErrorMessage);

        var zeroArgs = await parser.ParseAsync("/扩图", SessionWithImage());
        Assert.False(zeroArgs.Success);
        Assert.Contains("需先做裁切外扩", zeroArgs.ErrorMessage);
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

        // /全景 carries its own fixed explicit resolution, which must win over the injected one.
        var result = await parser.ParseAsync("/全景", SessionWithImage(), injected);

        Assert.True(result.Success);
        Assert.Equal(ResolutionMode.Explicit, result.Plan!.Resolution!.Mode);
        Assert.Equal(2048, result.Plan.Resolution.Width);
        Assert.Equal(1024, result.Plan.Resolution.Height);
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

    /// <summary>
    /// Loads the real <c>Template/commands.json</c> the App uses (not the built-in fallback)
    /// and exercises the variant / T2I / variadic fields through the source-generated JSON path.
    /// </summary>
    [Fact]
    public async Task Real_CommandsJson_Loads_Variants_And_Flags()
    {
        var path = FindCommandsFile();
        Assert.NotNull(path);
        var parser = new CommandParser(path!);

        var huan = Assert.Single(parser.Commands, command => command.Name == "/换背景");
        Assert.True(huan.Variadic);
        Assert.True(huan.Variants.ContainsKey("single"));
        Assert.True(huan.Variants.ContainsKey("multi"));

        var single = await parser.ParseAsync("/换背景 森林", SessionWithImage(), 1, resolution: null);
        var multi = await parser.ParseAsync("/换背景 森林", SessionWithImage(), 2, resolution: null);
        Assert.Contains("Replace the background", single.Plan!.Steps[0].Parameters["prompt"]);
        Assert.Contains("<image2>", multi.Plan!.Steps[0].Parameters["prompt"]);

        var generate = await parser.ParseAsync("/生成 森林精灵", SessionWithImage(), 0, resolution: null);
        Assert.Equal("", generate.Plan!.MainImagePath);
        Assert.Equal("森林精灵", generate.Plan.Steps[0].Parameters["prompt"]);

        var multiOnly = await parser.ParseAsync("/合照 两个人", SessionWithImage(), 1, resolution: null);
        Assert.False(multiOnly.Success);
        Assert.Contains("at least 2 images", multiOnly.ErrorMessage);
    }

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
    public async Task SlashCommand_SingleVariant_When_OneImage()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换背景 热带海滩", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        var prompt = Assert.Single(result.Plan!.Steps).Parameters["prompt"];
        Assert.Contains("Replace the background", prompt);
        Assert.Contains("热带海滩", prompt);
    }

    [Fact]
    public async Task SlashCommand_MultiVariant_When_TwoImages()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换背景 一片森林", SessionWithImage(), 2, resolution: null);

        Assert.True(result.Success);
        var prompt = Assert.Single(result.Plan!.Steps).Parameters["prompt"];
        Assert.Contains("<image2>", prompt);
        Assert.Contains("一片森林", prompt);
    }

    [Fact]
    public async Task SlashCommand_ChangeClothing_Both_Variants()
    {
        var parser = ParserWithoutFile();

        var single = await parser.ParseAsync("/换装 红色毛衣", SessionWithImage(), 1, resolution: null);
        var multi = await parser.ParseAsync("/换装 红色毛衣", SessionWithImage(), 2, resolution: null);

        Assert.True(single.Success);
        Assert.Contains("Change the clothing", Assert.Single(single.Plan!.Steps).Parameters["prompt"]);
        Assert.Contains("红色毛衣", Assert.Single(single.Plan.Steps).Parameters["prompt"]);

        Assert.True(multi.Success);
        Assert.Contains("garment from <image2>", Assert.Single(multi.Plan!.Steps).Parameters["prompt"]);
    }

    [Fact]
    public async Task SlashCommand_GroupPhoto_Needs_TwoImages()
    {
        var parser = ParserWithoutFile();

        var multi = await parser.ParseAsync("/合照 一起比心", SessionWithImage(), 2, resolution: null);
        var single = await parser.ParseAsync("/合照 一起比心", SessionWithImage(), 1, resolution: null);

        Assert.True(multi.Success);
        var prompt = Assert.Single(multi.Plan!.Steps).Parameters["prompt"];
        Assert.Contains("<image2>", prompt);
        Assert.Contains("一起比心", prompt);

        Assert.False(single.Success);
        Assert.Contains("at least 2 images", single.ErrorMessage);
    }

    [Fact]
    public async Task SlashCommand_Generate_Forces_Empty_MainImage()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/生成 森林精灵", SessionWithImage(), 0, resolution: null);

        Assert.True(result.Success);
        Assert.Equal("", result.Plan!.MainImagePath);
        Assert.Equal("森林精灵", Assert.Single(result.Plan.Steps).Parameters["prompt"]);
    }

    [Fact]
    public async Task SlashCommand_Variadic_Folds_MultiWord_Description()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换背景 一片 森林", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        Assert.Contains("一片 森林", Assert.Single(result.Plan!.Steps).Parameters["prompt"]);
    }

    [Fact]
    public async Task SlashCommand_Variadic_Empty_Returns_Error()
    {
        // /换背景 /换装 /合照 /换脸 opt into allow_empty_prompt; a variadic command that
        // does NOT (e.g. /换发色) keeps the guard.
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换发色", SessionWithImage(), 1, resolution: null);

        Assert.False(result.Success);
        Assert.Contains("你要换成什么发色", result.ErrorMessage);
    }

    [Fact]
    public async Task SlashCommand_GroupPhoto_Without_Description_Parses_With_TwoImages()
    {
        // /合照 opts into allow_empty_prompt: the two identities carry the content and the
        // description is an optional tail sentence.
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/合照", SessionWithImage(), 2, resolution: null);

        Assert.True(result.Success);
        Assert.Contains("stand together", Assert.Single(result.Plan!.Steps).Parameters["prompt"]);
    }

    [Fact]
    public async Task SlashCommand_GroupPhoto_Without_Description_Still_Needs_TwoImages()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/合照", SessionWithImage(), 1, resolution: null);

        Assert.False(result.Success);
        Assert.Contains("at least 2 images", result.ErrorMessage);
    }

    [Fact]
    public async Task SlashCommand_ChangeClothing_Without_Description_Parses()
    {
        // /换装 opts into allow_empty_prompt; the missing-argument guard is skipped (the tool
        // layer rejects an empty description with no reference image).
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换装", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        Assert.Equal("", Assert.Single(result.Plan!.Steps).Parameters["description"]);
    }

    [Fact]
    public async Task SlashCommand_FaceSwap_Without_Description_Parses_With_TwoImages()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换脸", SessionWithImage(), 2, resolution: null);

        Assert.True(result.Success);
        var prompt = Assert.Single(result.Plan!.Steps).Parameters["prompt"];
        Assert.Contains("head_swap", prompt);
        Assert.Contains("Picture 2", prompt);
    }

    [Fact]
    public async Task SlashCommand_FaceSwap_Without_Description_Needs_TwoImages()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/换脸", SessionWithImage(), 1, resolution: null);

        Assert.False(result.Success);
        Assert.Contains("at least 2 images", result.ErrorMessage);
    }

    [Fact]
    public async Task SlashCommand_AllowEmptyPrompt_Parses_Without_Description()
    {
        // A command that opts in via allow_empty_prompt parses with no description; the
        // missing-argument guard is skipped (the tool layer decides based on references).
        var directory = Path.Combine(Path.GetTempPath(), "zivai_aep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "commands.json");
        await File.WriteAllTextAsync(path, """
            {
              "version": "1.1",
              "commands": [
                { "name": "/换背景", "params": ["description"], "variadic": true,
                  "allow_empty_prompt": true, "tool": "QW21edit", "defaultVariant": "single",
                  "variants": { "single": "Replace the background of <image1> with: {description}." } }
              ]
            }
            """);

        try
        {
            var parser = new CommandParser(path);

            var result = await parser.ParseAsync("/换背景", SessionWithImage(), 1, resolution: null);

            Assert.True(result.Success);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Variadic_Command_Without_Params_Reports_Config_Error()
    {
        // A malformed command (variadic but declares no parameter) must report a config error
        // instead of the friendly "missing argument" question.
        var directory = Path.Combine(Path.GetTempPath(), "zivai_cmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "commands.json");
        await File.WriteAllTextAsync(path, """
            {
              "version": "1.1",
              "commands": [
                { "name": "/坏", "params": [], "variadic": true, "tool": "QW21edit", "template": "x" }
              ]
            }
            """);

        try
        {
            var parser = new CommandParser(path);

            var result = await parser.ParseAsync("/坏", SessionWithImage(), 1, resolution: null);

            Assert.False(result.Success);
            Assert.Contains("未声明任何参数", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Reload_Swaps_Command_Set_In_Place()
    {
        // Z-030 复议 (段 1): Reload replaces the held set in place. A command unknown to the
        // original set becomes parseable and a previously-known command disappears — observed
        // through the original parser reference, which is how the Executor / FlowRunner hold it.
        var parser = ParserWithoutFile(); // built-in set
        ICommandParser reference = parser;

        var before = await reference.ParseAsync("/去水印", SessionWithImage(), 1, resolution: null);
        Assert.True(before.Success);

        parser.Reload(new[]
        {
            new CommandDefinition
            {
                Name = "/新命令",
                Params = new List<string> { "description" },
                Tool = "QW21edit",
                Template = "do {description}",
            },
        });

        var oldCommand = await reference.ParseAsync("/去水印", SessionWithImage(), 1, resolution: null);
        Assert.False(oldCommand.Success);

        var newCommand = await reference.ParseAsync("/新命令 你好", SessionWithImage(), 1, resolution: null);
        Assert.True(newCommand.Success);
        Assert.Equal("/新命令", newCommand.MatchedCommand);
    }

    [Fact]
    public void Reload_Null_Throws()
    {
        var parser = ParserWithoutFile();

        Assert.Throws<ArgumentNullException>(() => parser.Reload(null!));
    }
}
