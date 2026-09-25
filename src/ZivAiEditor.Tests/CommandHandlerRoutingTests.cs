using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Execution;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// T3.1: handler routing (Edit / T2I / Outpaint / Tag), field-ownership warnings and the
/// handler input constraints. Pure parser; no LLM / GPU.
/// </summary>
public class CommandHandlerRoutingTests : IDisposable
{
    private readonly string _directory;

    public CommandHandlerRoutingTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_handler_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private CommandParser Parser(string commandsJson)
    {
        var path = Path.Combine(_directory, "commands.json");
        File.WriteAllText(path, commandsJson);
        return new CommandParser(path);
    }

    private static EditSession SessionWithImage()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\source.png");
        return session;
    }

    [Fact]
    public async Task Tag_Returns_Capability_Not_Plan()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [ { "name": "/打标", "handler": "Tag", "params": [] } ]
        }
        """);

        var result = await parser.ParseAsync("/打标", SessionWithImage(), 1, resolution: null);

        Assert.False(result.Success);
        Assert.Equal(CommandParser.TagCapability, result.Capability);
        Assert.Null(result.Plan);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Tag_With_Template_And_Tool_Reports_Warnings()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/打标", "handler": "Tag", "params": [], "template": "x", "tool": "QW21edit" }
          ]
        }
        """);

        var result = await parser.ParseAsync("/打标", SessionWithImage(), 1, resolution: null);

        Assert.Contains(result.Warnings, warning => warning.Contains("不支持 template"));
        Assert.Contains(result.Warnings, warning => warning.Contains("不支持 tool"));
    }

    [Fact]
    public async Task Tag_With_Two_Images_Is_Rejected()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [ { "name": "/打标", "handler": "Tag", "params": [] } ]
        }
        """);

        var result = await parser.ParseAsync("/打标", SessionWithImage(), 2, resolution: null);

        Assert.False(result.Success);
        Assert.Contains("恰好 1 张", result.ErrorMessage);
    }

    [Fact]
    public async Task Legacy_T2i_Maps_To_T2I_Handler()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/生成", "params": ["description"], "variadic": true, "tool": "QW21edit",
              "t2i": true, "template": "{description}" }
          ]
        }
        """);

        Assert.Equal(CommandHandler.T2I, Assert.Single(parser.Commands).EffectiveHandler);

        var result = await parser.ParseAsync("/生成 猫", SessionWithImage(), 0, resolution: null);

        Assert.True(result.Success);
        Assert.Equal("", result.Plan!.MainImagePath);
    }

    [Fact]
    public async Task T2I_With_Image_Is_Rejected()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/生成", "params": ["description"], "variadic": true, "tool": "QW21edit",
              "t2i": true, "template": "{description}" }
          ]
        }
        """);

        var result = await parser.ParseAsync("/生成 猫", SessionWithImage(), 1, resolution: null);

        Assert.False(result.Success);
        Assert.Contains("不接受输入图", result.ErrorMessage);
    }

    [Fact]
    public async Task Explicit_T2I_Handler_Without_T2i_Field_Succeeds()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/生成", "handler": "T2I", "params": ["description"], "variadic": true,
              "tool": "QW21edit", "template": "{description}" }
          ]
        }
        """);

        var result = await parser.ParseAsync("/生成 猫", SessionWithImage(), 0, resolution: null);

        Assert.True(result.Success);
        Assert.Equal("", result.Plan!.MainImagePath);
    }

    [Fact]
    public async Task Outpaint_With_Two_Images_Is_Rejected()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/扩图", "handler": "Outpaint", "params": ["width", "height"],
              "tool": "QW21outpaint", "template": "Extend to {width}x{height}." }
          ]
        }
        """);

        var result = await parser.ParseAsync("/扩图 2048 1280", SessionWithImage(), 2, resolution: null);

        Assert.False(result.Success);
        Assert.Contains("恰好 1 张", result.ErrorMessage);
    }

    [Fact]
    public async Task Outpaint_With_One_Image_Succeeds()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/扩图", "handler": "Outpaint", "params": ["width", "height"],
              "tool": "QW21outpaint", "template": "Extend to {width}x{height}." }
          ]
        }
        """);

        var result = await parser.ParseAsync("/扩图 2048 1280", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        Assert.Null(result.Capability);
        Assert.NotNull(result.Plan);
    }

    [Fact]
    public async Task Edit_Handler_Routes_To_Plan()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/换背景", "handler": "Edit", "params": ["description"], "variadic": true,
              "tool": "QW21edit", "template": "Replace with {description}." }
          ]
        }
        """);

        var result = await parser.ParseAsync("/换背景 森林", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        Assert.Null(result.Capability);
        Assert.Equal("QW21edit", Assert.Single(result.Plan!.Steps).ToolName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Explicit_Outpaint_With_T2i_Reports_Redundant()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/扩图", "handler": "Outpaint", "t2i": true, "params": ["width", "height"],
              "tool": "QW21outpaint", "template": "Extend to {width}x{height}." }
          ]
        }
        """);

        var result = await parser.ParseAsync("/扩图 2048 1280", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, warning => warning.Contains("t2i 为冗余"));
    }
}
