using ZivAiEditor.Agent.Execution;
using ZivAiEditor.App;
using ZivAiEditor.Contracts.Execution;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 9C.9-A1 (D3): the App-layer pre-gate that blocks a multi-only command (e.g. /合照)
/// before the parser rejects it. Uses the built-in command set (no file needed).
/// </summary>
public class CommandRequirementsTests
{
    private static IReadOnlyList<CommandDefinition> Commands()
        => new CommandParser(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"))
            .Commands;

    [Fact]
    public void MultiOnly_Command_Blocks_With_Fewer_Than_Two_Images()
    {
        var commands = Commands();

        Assert.True(CommandRequirements.RequiresMoreImages(
            commands, "/合照 两个人", currentImageCount: 0, attachmentCount: 0, out var hint));
        Assert.Contains("2 张图", hint);
        Assert.True(CommandRequirements.RequiresMoreImages(
            commands, "/合照 两个人", currentImageCount: 0, attachmentCount: 1, out _));
        Assert.True(CommandRequirements.RequiresMoreImages(
            commands, "/合照 两个人", currentImageCount: 1, attachmentCount: 0, out _));
    }

    [Fact]
    public void MultiOnly_Command_Allows_Two_Images()
    {
        var commands = Commands();

        // A single-image root (currentImageCount 1) + one attachment → effective 2.
        Assert.False(CommandRequirements.RequiresMoreImages(
            commands, "/合照 两个人", currentImageCount: 0, attachmentCount: 2, out _));
        Assert.False(CommandRequirements.RequiresMoreImages(
            commands, "/合照 两个人", currentImageCount: 1, attachmentCount: 1, out _));
        // A multi-image root alone (currentImageCount 2) → effective 2 (Step 9C.10-P2, R4).
        Assert.False(CommandRequirements.RequiresMoreImages(
            commands, "/合照 两个人", currentImageCount: 2, attachmentCount: 0, out _));
    }

    [Fact]
    public void Other_Commands_Are_Not_Blocked_By_Image_Count()
    {
        var commands = Commands();

        // /换背景 has a single variant → never blocked.
        Assert.False(CommandRequirements.RequiresMoreImages(
            commands, "/换背景 森林", currentImageCount: 1, attachmentCount: 0, out _));
        // flat-template commands.
        Assert.False(CommandRequirements.RequiresMoreImages(
            commands, "/去水印", currentImageCount: 1, attachmentCount: 0, out _));
        // T2I command ignores images.
        Assert.False(CommandRequirements.RequiresMoreImages(
            commands, "/生成 森林精灵", currentImageCount: 0, attachmentCount: 0, out _));
        // natural language.
        Assert.False(CommandRequirements.RequiresMoreImages(
            commands, "把天空换成日落", currentImageCount: 1, attachmentCount: 0, out _));
    }

    [Fact]
    public void OutpaintCrop_Gate_Blocks_Without_Outpaint_Crop()
    {
        var commands = Commands();

        Assert.True(CommandRequirements.RequiresOutpaintCrop(
            commands, "/扩图", hasOutpaintCrop: false, out var hint));
        Assert.Contains("裁切外扩", hint);
        Assert.False(CommandRequirements.RequiresOutpaintCrop(
            commands, "/扩图", hasOutpaintCrop: true, out _));
    }

    [Fact]
    public void OutpaintCrop_Gate_Ignores_Other_Commands_And_Text()
    {
        var commands = Commands();

        Assert.False(CommandRequirements.RequiresOutpaintCrop(
            commands, "/去水印", hasOutpaintCrop: false, out _));
        Assert.False(CommandRequirements.RequiresOutpaintCrop(
            commands, "把天空换成日落", hasOutpaintCrop: false, out _));
        Assert.False(CommandRequirements.RequiresOutpaintCrop(
            commands, null, hasOutpaintCrop: false, out _));
    }
}
