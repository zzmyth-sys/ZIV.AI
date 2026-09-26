using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// T5/S4-fix: pure ordering for the <c>/</c> suggestion list (pin <c>/扩图</c>, then usage count
/// descending, stable ties).
/// </summary>
public class CommandOrderingTests
{
    private static CommandDefinition Cmd(string name) => new() { Name = name };

    private static IReadOnlyDictionary<string, int> Counts(params (string Name, int Count)[] pairs)
        => pairs.ToDictionary(pair => pair.Name, pair => pair.Count, StringComparer.Ordinal);

    [Fact]
    public void Orders_By_Usage_Descending()
    {
        var commands = new[] { Cmd("/a"), Cmd("/b"), Cmd("/c") };
        var counts = Counts(("/a", 1), ("/b", 3), ("/c", 2));

        var ordered = CommandOrdering.Order(commands, counts);

        Assert.Equal(new[] { "/b", "/c", "/a" }, ordered.Select(c => c.Name));
    }

    [Fact]
    public void Ties_Preserve_Input_Order()
    {
        var commands = new[] { Cmd("/a"), Cmd("/b"), Cmd("/c") };
        var counts = Counts(("/a", 1), ("/b", 1), ("/c", 0));

        var ordered = CommandOrdering.Order(commands, counts);

        Assert.Equal(new[] { "/a", "/b", "/c" }, ordered.Select(c => c.Name));
    }

    [Fact]
    public void Pins_Outpaint_To_Front_Regardless_Of_Count()
    {
        var commands = new[] { Cmd("/a"), Cmd("/扩图"), Cmd("/b") };
        var counts = Counts(("/a", 5), ("/b", 1), ("/扩图", 0));

        var ordered = CommandOrdering.Order(commands, counts);

        Assert.Equal(new[] { "/扩图", "/a", "/b" }, ordered.Select(c => c.Name));
    }

    [Fact]
    public void Outpaint_Absent_Orders_By_Count()
    {
        var commands = new[] { Cmd("/a"), Cmd("/b") };
        var counts = Counts(("/a", 0), ("/b", 4));

        var ordered = CommandOrdering.Order(commands, counts);

        Assert.Equal(new[] { "/b", "/a" }, ordered.Select(c => c.Name));
    }

    [Fact]
    public void Empty_Counts_Keeps_Input_Order()
    {
        var commands = new[] { Cmd("/a"), Cmd("/b"), Cmd("/c") };

        var ordered = CommandOrdering.Order(commands, Counts());

        Assert.Equal(new[] { "/a", "/b", "/c" }, ordered.Select(c => c.Name));
    }

    [Fact]
    public void Missing_Counts_Are_Treated_As_Zero()
    {
        var commands = new[] { Cmd("/a"), Cmd("/b") };
        var counts = Counts(("/b", 2)); // /a absent

        var ordered = CommandOrdering.Order(commands, counts);

        Assert.Equal(new[] { "/b", "/a" }, ordered.Select(c => c.Name));
    }
}
