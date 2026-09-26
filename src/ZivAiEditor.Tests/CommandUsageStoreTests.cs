using ZivAiEditor.App;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// T5/S4-fix: file-backed usage counter (program-directory state). Each test runs in its own temp
/// directory; a missing / malformed file must never throw.
/// </summary>
public class CommandUsageStoreTests : IDisposable
{
    private readonly string _directory;

    public CommandUsageStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "zivai_usage_" + Guid.NewGuid().ToString("N"));
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

    private string FilePath => Path.Combine(_directory, CommandUsageStore.FileName);

    [Fact]
    public void Missing_File_Loads_Empty()
    {
        var store = new CommandUsageStore(_directory);

        var counts = store.Load();

        Assert.Empty(counts);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Record_Increments_And_Counts_Reflects_It()
    {
        var store = new CommandUsageStore(_directory);

        store.Record("/换背景");
        store.Record("/换背景");
        store.Record("/扩图");

        Assert.Equal(2, store.Counts["/换背景"]);
        Assert.Equal(1, store.Counts["/扩图"]);
    }

    [Fact]
    public void Record_Persists_And_Round_Trips()
    {
        var store = new CommandUsageStore(_directory);
        store.Record("/换背景");
        store.Record("/换背景");
        store.Record("/去水印");

        var reloaded = new CommandUsageStore(_directory).Load();

        Assert.Equal(2, reloaded["/换背景"]);
        Assert.Equal(1, reloaded["/去水印"]);
    }

    [Fact]
    public void Corrupt_File_Loads_Empty_Without_Throwing()
    {
        File.WriteAllText(FilePath, "{ this is not valid json");
        var store = new CommandUsageStore(_directory);

        var counts = store.Load();

        Assert.Empty(counts);
    }
}
