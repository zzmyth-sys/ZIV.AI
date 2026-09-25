using System.Text.Json;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// T2: the multi-slot LoRA field (<c>loras</c>) plus the legacy single-slot (<c>lora</c>)
/// upgrade. Pure JSON + the store's write normalization; no LLM / GPU.
/// </summary>
public class CommandLoraTests : IDisposable
{
    private readonly string _directory;

    public CommandLoraTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_lora_" + Guid.NewGuid().ToString("N"));
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

    private string UserPath => Path.Combine(_directory, CommandTemplateService.UserFileName);

    private static CommandDefinition Parse(string json)
        => JsonSerializer.Deserialize<CommandDefinition>(json)!;

    [Fact]
    public void Legacy_Single_Lora_Upgrades_To_Effective_Loras()
    {
        var command = Parse("{\"name\":\"/a\",\"lora\":{\"path\":\"p\",\"strength_model\":0.8}}");

        var lora = Assert.Single(command.EffectiveLoras);
        Assert.Equal("p", lora.Path);
        Assert.Equal(0.8, lora.StrengthModel!.Value);
    }

    [Fact]
    public void Multi_Loras_Are_Used_As_Is()
    {
        var command = Parse("{\"name\":\"/a\",\"loras\":[{\"path\":\"p1\"},{\"path\":\"p2\"}]}");

        Assert.Equal(new[] { "p1", "p2" }, command.EffectiveLoras.Select(lora => lora.Path));
    }

    [Fact]
    public void Both_Present_Loras_Wins()
    {
        var command = Parse(
            "{\"name\":\"/a\",\"lora\":{\"path\":\"old\"},\"loras\":[{\"path\":\"new\"}]}");

        Assert.Equal("new", Assert.Single(command.EffectiveLoras).Path);
    }

    [Fact]
    public void No_Lora_Yields_Empty_Effective_Loras()
    {
        var command = Parse("{\"name\":\"/a\"}");

        Assert.Empty(command.EffectiveLoras);
    }

    [Fact]
    public void Add_With_Legacy_Single_Lora_Writes_Loras_Only()
    {
        var service = new CommandTemplateService(_directory);

        service.Add(new CommandDefinition
        {
            Name = "/a",
            Tool = "QW21edit",
            Lora = new LoraOptions { Path = "p", StrengthModel = 0.8 },
        });

        var entry = JsonDocument.Parse(File.ReadAllText(UserPath)).RootElement.GetProperty("commands")[0];
        Assert.True(entry.TryGetProperty("loras", out var loras));
        Assert.False(entry.TryGetProperty("lora", out _));
        Assert.Equal("p", loras[0].GetProperty("path").GetString());
    }

    [Fact]
    public void Add_With_Multi_Loras_Keeps_Order()
    {
        var service = new CommandTemplateService(_directory);

        service.Add(new CommandDefinition
        {
            Name = "/a",
            Tool = "QW21edit",
            Loras = new List<LoraOptions>
            {
                new() { Path = "p1" },
                new() { Path = "p2" },
            },
        });

        var reloaded = service.List().Single(entry => entry.Definition.Name == "/a").Definition;
        Assert.Equal(new[] { "p1", "p2" }, reloaded.EffectiveLoras.Select(lora => lora.Path));
    }
}
