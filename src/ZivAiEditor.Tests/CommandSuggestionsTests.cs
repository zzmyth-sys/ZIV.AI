using ZivAiEditor.Agent.Execution;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// T5/S1: the pure prefix filter for the <c>/</c> command suggestion list. Matching is ordinal
/// and the source order is preserved.
/// </summary>
public class CommandSuggestionsTests
{
    private static CommandDefinition Cmd(string name) => new() { Name = name };

    private static IReadOnlyList<CommandDefinition> BuiltIn()
        => new CommandParser(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "commands.json"))
            .Commands;

    [Fact]
    public void Prefix_Matches_And_Preserves_Source_Order()
    {
        var commands = new List<CommandDefinition>
        {
            Cmd("/换背景"),
            Cmd("/换装"),
            Cmd("/去水印"),
            Cmd("/换发色"),
            Cmd("/换表情"),
        };

        var matches = CommandSuggestions.Filter(commands, "/换");

        Assert.Equal(new[] { "/换背景", "/换装", "/换发色", "/换表情" }, matches.Select(c => c.Name));
    }

    [Fact]
    public void Empty_Or_Slash_Prefix_Returns_All()
    {
        var commands = new List<CommandDefinition> { Cmd("/a"), Cmd("/b") };

        Assert.Equal(2, CommandSuggestions.Filter(commands, "/").Count);
        Assert.Equal(2, CommandSuggestions.Filter(commands, "").Count);
    }

    [Fact]
    public void No_Match_Returns_Empty()
    {
        var commands = new List<CommandDefinition> { Cmd("/换背景"), Cmd("/去水印") };

        Assert.Empty(CommandSuggestions.Filter(commands, "/不存在"));
    }

    [Fact]
    public void BuiltIn_Set_Counts()
    {
        var commands = BuiltIn();

        Assert.Equal(13, CommandSuggestions.Filter(commands, "/").Count);
        Assert.Equal(13, CommandSuggestions.Filter(commands, "").Count);
        Assert.Equal(5, CommandSuggestions.Filter(commands, "/换").Count);
        Assert.Empty(CommandSuggestions.Filter(commands, "/不存在"));
    }
}
