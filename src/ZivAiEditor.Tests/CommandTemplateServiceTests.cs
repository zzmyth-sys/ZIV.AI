using System.Text.Json;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// T2: built-in + user template data layering and CRUD (no LLM / GPU). Each test runs in its
/// own temp template directory so the built-in and user files never touch the repository.
/// </summary>
public class CommandTemplateServiceTests : IDisposable
{
    private readonly string _directory;

    public CommandTemplateServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_tpl_" + Guid.NewGuid().ToString("N"));
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

    private CommandTemplateService Service() => new(_directory);

    private string UserPath => Path.Combine(_directory, CommandTemplateService.UserFileName);

    private void WriteBuiltIn(params CommandDefinition[] commands)
        => File.WriteAllText(
            Path.Combine(_directory, CommandTemplateService.BuiltInFileName),
            JsonSerializer.Serialize(new { version = "1.0", commands }));

    private void WriteUser(params CommandDefinition[] commands)
        => File.WriteAllText(
            UserPath,
            JsonSerializer.Serialize(new { version = "1.0", commands }));

    private static CommandDefinition Command(
        string name,
        string tool = "QW21edit",
        CommandHandler handler = CommandHandler.Edit)
        => new() { Name = name, Tool = tool, Template = "tpl", Handler = handler };

    private static CommandTemplateDto Entry(IReadOnlyList<CommandTemplateDto> list, string name)
        => list.Single(entry => entry.Definition.Name == name);

    [Fact]
    public void List_Merges_User_Override_By_Name()
    {
        WriteBuiltIn(Command("/a", tool: "builtin"), Command("/b"));
        WriteUser(Command("/a", tool: "user"));

        var list = Service().List();

        Assert.Equal(2, list.Count);
        Assert.Equal(CommandSource.User, Entry(list, "/a").Source);
        Assert.Equal("user", Entry(list, "/a").Definition.Tool);
        Assert.Equal(CommandSource.BuiltIn, Entry(list, "/b").Source);
    }

    [Fact]
    public void List_Appends_New_User_Name_After_BuiltIn()
    {
        WriteBuiltIn(Command("/a"));
        WriteUser(Command("/z"));

        var list = Service().List();

        Assert.Equal(new[] { "/a", "/z" }, list.Select(entry => entry.Definition.Name));
        Assert.Equal(CommandSource.User, Entry(list, "/z").Source);
    }

    [Fact]
    public void Missing_User_File_Lists_BuiltIn_Only()
    {
        WriteBuiltIn(Command("/a"));

        Assert.False(File.Exists(UserPath));
        var list = Service().List();

        Assert.Equal(CommandSource.BuiltIn, Assert.Single(list).Source);
    }

    [Fact]
    public void Missing_BuiltIn_Falls_Back_To_Default_Commands()
    {
        var list = Service().List();

        Assert.Contains(list, entry => entry.Definition.Name == "/换背景");
        Assert.All(list, entry => Assert.Equal(CommandSource.BuiltIn, entry.Source));
    }

    [Fact]
    public void Add_Writes_User_File()
    {
        WriteBuiltIn(Command("/a"));
        var service = Service();

        service.Add(Command("/new"));

        Assert.True(File.Exists(UserPath));
        Assert.Equal(CommandSource.User, Entry(service.List(), "/new").Source);
    }

    [Fact]
    public void Update_Replaces_User_Entry()
    {
        var service = Service();
        service.Add(Command("/x", tool: "v1"));

        service.Update("/x", Command("/x", tool: "v2"));

        var list = service.List();
        Assert.Single(list, entry => entry.Definition.Name == "/x");
        Assert.Equal("v2", Entry(list, "/x").Definition.Tool);
    }

    [Fact]
    public void Delete_Reverts_To_BuiltIn()
    {
        WriteBuiltIn(Command("/a", tool: "builtin"));
        var service = Service();
        service.Add(Command("/a", tool: "user"));
        Assert.Equal("user", Entry(service.List(), "/a").Definition.Tool);

        service.Delete("/a");

        var list = service.List();
        Assert.Equal(CommandSource.BuiltIn, Entry(list, "/a").Source);
        Assert.Equal("builtin", Entry(list, "/a").Definition.Tool);
    }

    [Fact]
    public void Reset_Reverts_To_BuiltIn()
    {
        WriteBuiltIn(Command("/a", tool: "builtin"));
        var service = Service();
        service.Add(Command("/a", tool: "user"));

        service.Reset("/a");

        Assert.Equal(CommandSource.BuiltIn, Entry(service.List(), "/a").Source);
    }

    [Fact]
    public void ResetAll_Removes_User_File()
    {
        WriteBuiltIn(Command("/a"));
        var service = Service();
        service.Add(Command("/z"));
        Assert.True(File.Exists(UserPath));

        service.ResetAll();

        Assert.False(File.Exists(UserPath));
        Assert.DoesNotContain(service.List(), entry => entry.Definition.Name == "/z");
    }

    [Fact]
    public void Deleting_Last_User_Entry_Removes_File()
    {
        var service = Service();
        service.Add(Command("/only"));

        service.Delete("/only");

        Assert.False(File.Exists(UserPath));
    }

    [Fact]
    public void Handler_Defaults_To_Edit_When_Json_Omits_It()
    {
        var definition = JsonSerializer.Deserialize<CommandDefinition>(
            "{\"name\":\"/x\",\"tool\":\"QW21edit\"}");

        Assert.NotNull(definition);
        Assert.Equal(CommandHandler.Edit, definition!.Handler);
    }

    [Fact]
    public void Handler_Parses_From_String()
    {
        var definition = JsonSerializer.Deserialize<CommandDefinition>(
            "{\"name\":\"/tag\",\"handler\":\"Tag\"}");

        Assert.NotNull(definition);
        Assert.Equal(CommandHandler.Tag, definition!.Handler);
    }

    [Fact]
    public void User_Handler_Survives_Round_Trip()
    {
        var service = Service();
        service.Add(Command("/tag", tool: "tagger", handler: CommandHandler.Tag));

        var reloaded = Service().List();

        Assert.Equal(CommandHandler.Tag, Entry(reloaded, "/tag").Definition.Handler);
    }

    [Fact]
    public void NormalizeLora_Preserves_AllowEmptyPrompt_On_Legacy_SingleSlot_RoundTrip()
    {
        // Regression: a legacy single-slot `lora` forces NormalizeLora to rebuild the definition
        // on the write path (Add -> Upsert -> WriteUserEntries); AllowEmptyPrompt must survive the
        // rebuild (it was silently dropped when the rebuild initializer omitted it). Built via the
        // service (NOT WriteUser, which bypasses NormalizeLora) so the bug would actually reproduce.
        var service = Service();
        service.Add(new CommandDefinition
        {
            Name = "/legacy",
            Tool = "QW21edit",
            Template = "tpl",
            Variadic = true,
            AllowEmptyPrompt = true,
            Lora = new LoraOptions { Path = "face-swap", StrengthModel = 0.8, StrengthClip = 0.7 },
        });

        var definition = Entry(Service().List(), "/legacy").Definition;

        Assert.True(definition.AllowEmptyPrompt);
        var lora = Assert.Single(definition.EffectiveLoras);
        Assert.Equal("face-swap", lora.Path);
        Assert.Equal(0.8, lora.StrengthModel!.Value);
        Assert.Equal(0.7, lora.StrengthClip!.Value);
    }
}
