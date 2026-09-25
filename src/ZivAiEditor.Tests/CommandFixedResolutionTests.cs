using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// P1a: command-owned fixed resolution and the generalized <c>width</c>/<c>height</c>
/// resolution path. Precedence: fixed_resolution &gt; width/height arguments &gt; UI tier.
/// Pure parser; no LLM / GPU.
/// </summary>
public class CommandFixedResolutionTests : IDisposable
{
    private readonly string _directory;

    public CommandFixedResolutionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_fixedres_" + Guid.NewGuid().ToString("N"));
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
    public async Task FixedResolution_Wins_Over_Width_Height_Args()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/全景", "handler": "Edit", "params": ["width", "height"],
              "tool": "QW21edit",
              "fixed_resolution": { "mode": "Explicit", "width": 2048, "height": 1024 },
              "template": "Panorama {width}x{height}." }
          ]
        }
        """);

        var result = await parser.ParseAsync("/全景 800 600", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        var resolution = Assert.IsType<ResolutionPolicy>(result.Plan!.Resolution);
        Assert.Equal(ResolutionMode.Explicit, resolution.Mode);
        Assert.Equal(2048, resolution.Width);
        Assert.Equal(1024, resolution.Height);
    }

    [Fact]
    public async Task Width_Height_Args_Win_Over_Injected_Resolution()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/裁幅", "handler": "Edit", "params": ["width", "height"],
              "tool": "QW21edit", "template": "Crop to {width}x{height}." }
          ]
        }
        """);
        var injected = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 };

        var result = await parser.ParseAsync("/裁幅 1024 768", SessionWithImage(), 1, injected);

        Assert.True(result.Success);
        var resolution = Assert.IsType<ResolutionPolicy>(result.Plan!.Resolution);
        Assert.Equal(ResolutionMode.Explicit, resolution.Mode);
        Assert.Equal(1024, resolution.Width);
        Assert.Equal(768, resolution.Height);
    }

    [Fact]
    public async Task Outpaint_Width_Height_Still_Set_Explicit()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/外扩", "handler": "Outpaint", "params": ["width", "height"],
              "tool": "QW21outpaint", "template": "Extend to {width}x{height}." }
          ]
        }
        """);

        var result = await parser.ParseAsync("/外扩 2048 1280", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        var resolution = Assert.IsType<ResolutionPolicy>(result.Plan!.Resolution);
        Assert.Equal(ResolutionMode.Explicit, resolution.Mode);
        Assert.Equal(2048, resolution.Width);
        Assert.Equal(1280, resolution.Height);
    }

    [Fact]
    public async Task Tag_With_FixedResolution_Reports_Warning_And_Ignores()
    {
        var parser = Parser("""
        {
          "version": "1.1",
          "commands": [
            { "name": "/打标", "handler": "Tag", "params": [],
              "fixed_resolution": { "mode": "Explicit", "width": 2048, "height": 1024 } }
          ]
        }
        """);

        var result = await parser.ParseAsync("/打标", SessionWithImage(), 1, resolution: null);

        Assert.False(result.Success);
        Assert.Equal(CommandParser.TagCapability, result.Capability);
        Assert.Contains(result.Warnings, warning => warning.Contains("不支持 fixed_resolution"));
    }
}
