using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Agent.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Step 8-1: a LoRA declared in <c>Template/commands.json</c> is carried data-driven into the
/// plan's <see cref="ZivAiEditor.Contracts.Execution.EditStep.Lora"/>. No code change is needed
/// to add a LoRA template — only the data file changes.
/// </summary>
public class CommandParserLoraTests
{
    private const string SourceImage = @"C:\img\source.png";

    private static string WriteCommands(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "commands.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static EditSession SessionWithImage()
    {
        var session = new EditSession();
        session.SetRoot(SourceImage);
        return session;
    }

    [Fact]
    public async Task Command_With_Lora_Populates_EditStep_Lora()
    {
        const string json = """
        {
          "version": "1.1",
          "commands": [
            {
              "name": "/动漫",
              "params": [],
              "tool": "QW21edit",
              "template": "make it anime style",
              "lora": { "path": "anime_v2", "strength_model": 0.8, "strength_clip": 0.7 }
            }
          ]
        }
        """;
        var parser = new CommandParser(WriteCommands(json));

        var result = await parser.ParseAsync("/动漫", SessionWithImage());

        Assert.True(result.Success);
        var step = Assert.Single(result.Plan!.Steps);
        var lora = Assert.Single(step.EffectiveLoras);
        Assert.Equal("anime_v2", lora.Path);
        Assert.Equal(0.8, lora.StrengthModel!.Value);
        Assert.Equal(0.7, lora.StrengthClip!.Value);
    }

    [Fact]
    public async Task Command_With_Lora_Id_Only_Uses_Default_Strengths()
    {
        const string json = """
        {
          "version": "1.1",
          "commands": [
            {
              "name": "/简",
              "params": [],
              "tool": "QW21edit",
              "template": "x",
              "lora": { "path": "anime_v2" }
            }
          ]
        }
        """;
        var parser = new CommandParser(WriteCommands(json));

        var result = await parser.ParseAsync("/简", SessionWithImage());

        Assert.True(result.Success);
        var lora = Assert.Single(Assert.Single(result.Plan!.Steps).EffectiveLoras);
        Assert.Equal("anime_v2", lora.Path);
        Assert.Equal(1.0, lora.StrengthModel!.Value);
        Assert.Equal(1.0, lora.StrengthClip!.Value);
    }

    [Fact]
    public async Task Command_With_Explicit_Zero_Strength_Preserves_Zero()
    {
        const string json = """
        {
          "version": "1.1",
          "commands": [
            {
              "name": "/抑制",
              "params": [],
              "tool": "QW21edit",
              "template": "x",
              "lora": { "path": "anime_v2", "strength_model": 0, "strength_clip": 0 }
            }
          ]
        }
        """;
        var parser = new CommandParser(WriteCommands(json));

        var result = await parser.ParseAsync("/抑制", SessionWithImage());

        Assert.True(result.Success);
        var lora = Assert.Single(Assert.Single(result.Plan!.Steps).EffectiveLoras);
        // Step 8-2: an explicit 0 must survive (0 = suppress), not be rewritten to 1.0.
        Assert.Equal(0.0, lora.StrengthModel!.Value);
        Assert.Equal(0.0, lora.StrengthClip!.Value);
    }

    [Fact]
    public async Task Command_Without_Lora_Leaves_Step_Lora_Null()
    {
        const string json = """
        {
          "version": "1.1",
          "commands": [
            { "name": "/plain", "params": [], "tool": "QW21edit", "template": "do it" }
          ]
        }
        """;
        var parser = new CommandParser(WriteCommands(json));

        var result = await parser.ParseAsync("/plain", SessionWithImage());

        Assert.True(result.Success);
        var step = Assert.Single(result.Plan!.Steps);
        Assert.Null(step.Loras);
        Assert.Empty(step.EffectiveLoras);
    }

    [Fact]
    public async Task Command_With_Multiple_Loras_Populates_EditStep_Loras()
    {
        const string json = """
        {
          "version": "1.1",
          "commands": [
            {
              "name": "/双",
              "params": [],
              "tool": "QW21edit",
              "template": "x",
              "loras": [
                { "path": "lora_a", "strength_model": 0.8 },
                { "path": "lora_b", "strength_model": 0.6 }
              ]
            }
          ]
        }
        """;
        var parser = new CommandParser(WriteCommands(json));

        var result = await parser.ParseAsync("/双", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        var loras = Assert.Single(result.Plan!.Steps).EffectiveLoras;
        Assert.Equal(new[] { "lora_a", "lora_b" }, loras.Select(lora => lora.Path));
        Assert.Equal(0.8, loras[0].StrengthModel!.Value);
        Assert.Equal(0.6, loras[1].StrengthModel!.Value);
    }

    [Fact]
    public async Task Duplicate_Lora_Paths_Are_Deduplicated_With_Warning()
    {
        const string json = """
        {
          "version": "1.1",
          "commands": [
            {
              "name": "/重复",
              "params": [],
              "tool": "QW21edit",
              "template": "x",
              "loras": [ { "path": "same" }, { "path": "same" }, { "path": "other" } ]
            }
          ]
        }
        """;
        var parser = new CommandParser(WriteCommands(json));

        var result = await parser.ParseAsync("/重复", SessionWithImage(), 1, resolution: null);

        Assert.True(result.Success);
        var loras = Assert.Single(result.Plan!.Steps).EffectiveLoras;
        Assert.Equal(new[] { "same", "other" }, loras.Select(lora => lora.Path));
        Assert.Contains(result.Warnings, warning => warning.Contains("重复 path"));
    }
}
