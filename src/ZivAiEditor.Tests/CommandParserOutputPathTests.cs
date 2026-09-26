using System;
using System.IO;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Bridge §4.1-4: the CommandParser outputPath overload (no GPU).</summary>
public class CommandParserOutputPathTests
{
    private const string SourceImage = @"C:\img\source.png";
    private const string Output = @"C:\img\source_quick.png";

    private static CommandParser Parser()
        => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"));

    private static EditSession Session()
    {
        var session = new EditSession();
        session.SetRoot(SourceImage);
        return session;
    }

    [Fact]
    public async Task NewOverload_InjectsOutputPath()
    {
        var result = await Parser().ParseAsync("/去水印", Session(), imageCount: 1, resolution: null, Output);

        Assert.True(result.Success);
        var step = Assert.Single(result.Plan!.Steps);
        Assert.Equal(Output, step.Parameters["output_path"]);
    }

    [Fact]
    public async Task ExistingOverload_DoesNotInjectOutputPath()
    {
        var result = await Parser().ParseAsync("/去水印", Session());

        Assert.True(result.Success);
        var step = Assert.Single(result.Plan!.Steps);
        Assert.False(step.Parameters.ContainsKey("output_path"));
    }

    [Fact]
    public async Task NewOverload_BlankOutputPath_LeavesPlanUnchanged()
    {
        var result = await Parser().ParseAsync("/去水印", Session(), imageCount: 1, resolution: null, "  ");

        Assert.True(result.Success);
        var step = Assert.Single(result.Plan!.Steps);
        Assert.False(step.Parameters.ContainsKey("output_path"));
    }
}
