using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// P1 · <c>/扩图</c>: the parser's name-based outpaint path requires the current node's
/// crop-tool outpaint crop, runs maskless on the (blue-padded) canvas, and carries no
/// resolution of its own — the UI-selected tier applies like every other command.
/// Pure parser; no GPU.
/// </summary>
public class CommandOutpaintTests
{
    private static CommandParser ParserWithoutFile()
        => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"));

    private static EditSession SessionWithImage()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\source.png");
        return session;
    }

    private static EditSession SessionWithOutpaintCrop()
    {
        var session = SessionWithImage();
        var node = session.GetHistory()[0];
        session.SetNodeCrop(node.NodeId, new CropSpec
        {
            X = -50, Y = -50, Width = 300, Height = 200,
            SourceWidth = 200, SourceHeight = 160,
        });
        return session;
    }

    [Fact]
    public async Task Outpaint_With_Crop_Succeeds_Maskless_Without_Owning_Resolution()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/扩图", SessionWithOutpaintCrop());

        Assert.True(result.Success);
        Assert.Equal("/扩图", result.MatchedCommand);
        var plan = result.Plan!;
        Assert.Null(plan.Mask); // reference-conditioned: no diffusion mask
        Assert.Null(plan.Resolution); // the UI-selected tier applies (parser owns none)
        var step = Assert.Single(plan.Steps);
        Assert.Equal("QW21edit", step.ToolName);
        Assert.Contains("Outpaint the image", step.Parameters["prompt"]);
    }

    [Fact]
    public async Task Outpaint_Follows_The_Injected_Ui_Resolution()
    {
        var parser = ParserWithoutFile();
        var injected = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1024 };

        var result = await parser.ParseAsync("/扩图", SessionWithOutpaintCrop(), 1, injected);

        Assert.True(result.Success);
        var resolution = Assert.IsType<ResolutionPolicy>(result.Plan!.Resolution);
        Assert.Equal(ResolutionMode.Side, resolution.Mode);
        Assert.Equal(1024, resolution.Side);
    }

    [Fact]
    public async Task Outpaint_Without_Outpaint_Crop_Fails_With_Hint()
    {
        var parser = ParserWithoutFile();

        var result = await parser.ParseAsync("/扩图", SessionWithImage());

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.Contains("需先做裁切外扩", result.ErrorMessage);
    }

    [Fact]
    public async Task Outpaint_With_Arguments_Is_A_Parse_Error()
    {
        var parser = ParserWithoutFile();

        // Params are empty, so the former /扩图 width/height arguments no longer parse.
        var result = await parser.ParseAsync("/扩图 2048 1280", SessionWithImage());

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.NotNull(result.ErrorMessage);
    }
}
