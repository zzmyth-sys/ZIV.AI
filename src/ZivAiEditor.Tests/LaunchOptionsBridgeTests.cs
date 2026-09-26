using ZivAiEditor.UI;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Bridge §10: the new CLI fields, IsQuick and the IsEmpty guard (no GPU).</summary>
public class LaunchOptionsBridgeTests
{
    [Fact]
    public void Parse_ReadsAllBridgeFlags()
    {
        var options = LaunchOptions.Parse(new[]
        {
            "--image", @"C:\a.png",
            "--quick", "/去水印",
            "--output", @"C:\a_quick.png",
            "--notify", @"C:\temp\n.json",
            "--resolution", "balanced",
        });

        Assert.Equal(@"C:\a.png", options.ImagePath);
        Assert.Equal("/去水印", options.QuickTemplateId);
        Assert.Equal(@"C:\a_quick.png", options.OutputPath);
        Assert.Equal(@"C:\temp\n.json", options.NotifyPath);
        Assert.Equal("balanced", options.Resolution);
        Assert.True(options.IsQuick);
        Assert.False(options.IsEmpty);
    }

    [Fact]
    public void IsEmpty_False_WhenOnlyANewFieldIsSet()
    {
        Assert.False(LaunchOptions.Parse(new[] { "--notify", @"C:\n.json" }).IsEmpty);
        Assert.False(LaunchOptions.Parse(new[] { "--output", @"C:\o.png" }).IsEmpty);
        Assert.False(LaunchOptions.Parse(new[] { "--quick", "/去背景" }).IsEmpty);
        Assert.False(LaunchOptions.Parse(new[] { "--resolution", "fast" }).IsEmpty);
        Assert.True(LaunchOptions.Parse(new[] { "--unknown", "x" }).IsEmpty);
    }

    [Fact]
    public void IsQuick_RequiresATemplateId()
    {
        Assert.False(LaunchOptions.Parse(new[] { "--quick", "--image", "a.png" }).IsQuick);
        Assert.True(LaunchOptions.Parse(new[] { "--quick", "/全景" }).IsQuick);
    }

    [Fact]
    public void Parse_MissingValues_NeverThrows()
    {
        var options = LaunchOptions.Parse(new[] { "--quick", "--output" });

        Assert.False(options.IsQuick);
        Assert.Null(options.OutputPath);
    }
}
