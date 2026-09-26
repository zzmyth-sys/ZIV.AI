using System;
using System.IO;
using System.Text.Json;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Bridge §4.1-3: CommandDefinition.quick / shortcut_label + NormalizeLora preservation.</summary>
public class CommandTemplateQuickPreservationTests
{
    [Fact]
    public void CommandDefinition_Json_MapsQuickAndShortcutLabel()
    {
        var json = """
        { "name": "/去水印", "quick": true, "shortcut_label": "去水印" }
        """;

        var command = JsonSerializer.Deserialize<CommandDefinition>(json)!;

        Assert.True(command.Quick);
        Assert.Equal("去水印", command.ShortcutLabel);
    }

    [Fact]
    public void NormalizeLora_PreservesQuickLabelAndFixedResolution()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zivai_tpl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var service = new CommandTemplateService(directory);
        service.Add(new CommandDefinition
        {
            Name = "/全景",
            Params = new System.Collections.Generic.List<string>(),
            Tool = "QW21edit",
            Template = "panorama",
            Quick = true,
            ShortcutLabel = "全景",
            FixedResolution = new ResolutionPolicy
            {
                Mode = ResolutionMode.Explicit,
                Width = 2048,
                Height = 1024,
            },
            Lora = new LoraOptions { Path = "style.safetensors" },
        });

        using var document = JsonDocument.Parse(File.ReadAllText(service.UserFilePath));
        var command = document.RootElement.GetProperty("commands")[0];

        Assert.True(command.GetProperty("quick").GetBoolean());
        Assert.Equal("全景", command.GetProperty("shortcut_label").GetString());
        Assert.Equal(2048, command.GetProperty("fixed_resolution").GetProperty("width").GetInt32());
        Assert.False(command.TryGetProperty("lora", out _)); // folded into loras
        Assert.True(command.GetProperty("loras").GetArrayLength() > 0);
    }
}
