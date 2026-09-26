using ZivAiEditor.App;
using ZivAiEditor.Contracts.Execution;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// T5/S1: the pure context-availability evaluator behind the <c>/</c> list (S4) and the
/// <see cref="CommandRequirements"/> pre-gate. Covers each axis plus the combined cases.
/// </summary>
public class CommandAvailabilityTests
{
    private static CommandAvailability.Context Ctx(
        bool hasImage = true,
        int imageCount = 1,
        bool hasOutpaintCrop = true)
        => new(hasImage, imageCount, hasOutpaintCrop);

    private static CommandDefinition Edit(string name = "/换背景", params string[] variants)
    {
        var definition = new CommandDefinition { Name = name };
        foreach (var variant in variants)
        {
            definition.Variants[variant] = "tpl";
        }

        return definition;
    }

    private static CommandDefinition T2I()
        => new() { Name = "/生成", Handler = CommandHandler.T2I };

    [Fact]
    public void T2I_Without_Image_Is_Available()
    {
        var (available, reason) = CommandAvailability.Evaluate(T2I(), Ctx(hasImage: false, imageCount: 0));

        Assert.True(available);
        Assert.Null(reason);
    }

    [Fact]
    public void T2I_With_Image_Is_Unavailable()
    {
        var (available, reason) = CommandAvailability.Evaluate(T2I(), Ctx(hasImage: true));

        Assert.False(available);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Legacy_T2i_Flag_Maps_To_T2I()
    {
        var legacy = new CommandDefinition { Name = "/legacy", T2i = true };

        var (available, _) = CommandAvailability.Evaluate(legacy, Ctx(hasImage: true));

        Assert.False(available);
    }

    [Fact]
    public void Edit_With_Image_Is_Available()
    {
        var (available, reason) = CommandAvailability.Evaluate(Edit(), Ctx(hasImage: true));

        Assert.True(available);
        Assert.Null(reason);
    }

    [Fact]
    public void Edit_Without_Image_Is_Unavailable()
    {
        var (available, reason) = CommandAvailability.Evaluate(Edit(), Ctx(hasImage: false, imageCount: 0));

        Assert.False(available);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void MultiOnly_With_One_Image_Is_Unavailable()
    {
        var (available, reason) = CommandAvailability.Evaluate(Edit("/合照", "multi"), Ctx(imageCount: 1));

        Assert.False(available);
        Assert.Contains("2", reason);
    }

    [Fact]
    public void MultiOnly_With_Two_Images_Is_Available()
    {
        var (available, _) = CommandAvailability.Evaluate(Edit("/合照", "multi"), Ctx(imageCount: 2));

        Assert.True(available);
    }

    [Fact]
    public void Outpaint_Without_Crop_Is_Unavailable()
    {
        var (available, reason) = CommandAvailability.Evaluate(
            Edit("/扩图"), Ctx(hasOutpaintCrop: false));

        Assert.False(available);
        Assert.Contains("裁切外扩", reason);
    }

    [Fact]
    public void Outpaint_With_Crop_Is_Available()
    {
        var (available, _) = CommandAvailability.Evaluate(
            Edit("/扩图"), Ctx(hasOutpaintCrop: true));

        Assert.True(available);
    }

    [Fact]
    public void Single_And_Multi_Variants_Are_Available_With_One_Image()
    {
        var (available, _) = CommandAvailability.Evaluate(
            Edit("/换背景", "single", "multi"), Ctx(imageCount: 1));

        Assert.True(available);
    }
}
