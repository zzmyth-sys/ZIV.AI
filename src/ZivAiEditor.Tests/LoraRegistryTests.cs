using System.IO;
using ZivAiEditor.Backend;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Backend reader for <c>Template/loras.json</c> (display-only path / description lookup for the
/// LoRA control). Each test uses its own temp file; a missing / corrupt file must degrade to an
/// empty table without throwing (mirrors <c>ModelProfileRegistry</c>).
/// </summary>
public class LoraRegistryTests : IDisposable
{
    private readonly string _directory;

    public LoraRegistryTests()
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

    private string WriteRegistry(string json)
    {
        var path = Path.Combine(_directory, "loras.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Loads_Valid_Registry_And_Maps_Fields()
    {
        var path = WriteRegistry(
            """
            {
              "version": "1",
              "loras": [
                {
                  "id": "face-swap",
                  "path": "C:/x/bfs.safetensors",
                  "default_strength_model": 0.8,
                  "default_strength_clip": 0.7,
                  "description": "换头"
                }
              ]
            }
            """);

        var registry = new LoraRegistry(path);
        var entry = registry.TryGet("face-swap");

        Assert.NotNull(entry);
        Assert.Equal("C:/x/bfs.safetensors", entry!.Path);
        Assert.Equal("换头", entry.Description);
        Assert.Equal(0.8, entry.DefaultStrengthModel!.Value);
        Assert.Equal(0.7, entry.DefaultStrengthClip!.Value);
        Assert.Single(registry.All);
    }

    [Fact]
    public void Missing_File_Yields_Empty_And_Does_Not_Throw()
    {
        var registry = new LoraRegistry(Path.Combine(_directory, "nope.json"));

        Assert.Null(registry.TryGet("face-swap"));
        Assert.Empty(registry.All);
    }

    [Fact]
    public void Corrupt_Json_Yields_Empty_And_Does_Not_Throw()
    {
        var registry = new LoraRegistry(WriteRegistry("{ this is not json "));

        Assert.Empty(registry.All);
        Assert.Null(registry.TryGet("face-swap"));
    }

    [Fact]
    public void Blank_Ids_Are_Skipped_And_All_Returns_Every_Valid_Entry()
    {
        var path = WriteRegistry(
            """
            {
              "loras": [
                { "path": "a" },
                { "id": "", "path": "b" },
                { "id": "ok", "path": "c" }
              ]
            }
            """);

        var registry = new LoraRegistry(path);

        Assert.Single(registry.All);
        Assert.Equal("c", registry.TryGet("ok")!.Path);
    }

    [Fact]
    public void TryGet_Is_Case_Insensitive_And_Trims()
    {
        var registry = new LoraRegistry(WriteRegistry(
            """{ "loras": [ { "id": "face-swap", "path": "p" } ] }"""));

        Assert.NotNull(registry.TryGet("FACE-SWAP"));
        Assert.NotNull(registry.TryGet(" face-swap "));
        Assert.Null(registry.TryGet(""));
    }
}
