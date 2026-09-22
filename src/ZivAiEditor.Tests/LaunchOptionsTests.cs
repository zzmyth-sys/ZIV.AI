using ZivAiEditor.UI;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9A CLI parsing tests (pure logic, no UI thread, no GPU).</summary>
public class LaunchOptionsTests
{
    [Fact]
    public void Parses_Image_Prompt_And_Mask()
    {
        var options = LaunchOptions.Parse(new[]
        {
            "--image", @"C:\img\a.png",
            "--prompt", "换背景 茶肆",
            "--mask", @"C:\img\m.png",
        });

        Assert.Equal(@"C:\img\a.png", options.ImagePath);
        Assert.Equal("换背景 茶肆", options.Prompt);
        Assert.Equal(@"C:\img\m.png", options.MaskPath);
        Assert.False(options.IsEmpty);
    }

    [Fact]
    public void No_Args_Produces_Empty_Options()
    {
        Assert.True(LaunchOptions.Parse(Array.Empty<string>()).IsEmpty);
        Assert.True(LaunchOptions.Parse(null).IsEmpty);
        Assert.Null(LaunchOptions.Parse(null).ImagePath);
    }

    [Fact]
    public void Missing_Value_Is_Ignored()
    {
        Assert.Null(LaunchOptions.Parse(new[] { "--image" }).ImagePath);

        var trailing = LaunchOptions.Parse(new[] { "--image", "--prompt", "hi" });
        Assert.Null(trailing.ImagePath);
        Assert.Equal("hi", trailing.Prompt);
    }

    [Fact]
    public void Unknown_Flags_Are_Ignored()
    {
        var options = LaunchOptions.Parse(new[] { "--unknown", "x", "--image", @"C:\a.png" });

        Assert.Equal(@"C:\a.png", options.ImagePath);
        Assert.Null(options.Prompt);
    }
}
