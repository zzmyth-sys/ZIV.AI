using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Batch 1 · A5 tests: the project-directory path guard (defense-in-depth against path
/// traversal) and its wiring into the project catalog / session store. File IO only, no GPU.
/// </summary>
public class PathSanitizerTests
{
    private static string NewRoot()
        => Path.Combine(Path.GetTempPath(), "zivai_sanitizer_" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("../")]
    [InlineData("..\\")]
    [InlineData("../evil")]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/abs")]
    [InlineData("\\abs")]
    public void IsSafe_Rejects_Traversal_And_Separator_Ids(string? id)
    {
        Assert.False(PathSanitizer.IsSafe(id, NewRoot()));
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void IsSafe_Accepts_Flat_Ids(string id)
    {
        Assert.True(PathSanitizer.IsSafe(id, NewRoot()));
    }

    [Fact]
    public void ResolveDirectory_Unsafe_Id_Does_Not_Exist()
    {
        var resolved = PathSanitizer.ResolveDirectory("../evil", NewRoot());

        Assert.False(Directory.Exists(resolved));
        Assert.False(File.Exists(resolved));
    }

    [Fact]
    public async Task DeleteAsync_Unsafe_Id_Deletes_Nothing()
    {
        var root = NewRoot();
        var outside = NewRoot();

        try
        {
            // A sibling directory named like the traversal target: root/../outside-candidate.
            var victim = Path.Combine(Path.GetDirectoryName(root)!, "zivai_victim_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(victim);
            try
            {
                var projects = new ProjectService(new SessionStore(root));
                await projects.DeleteAsync("../" + Path.GetFileName(victim));

                Assert.True(Directory.Exists(victim));
            }
            finally
            {
                Cleanup(victim);
            }
        }
        finally
        {
            Cleanup(root);
            Cleanup(outside);
        }
    }

    [Fact]
    public async Task RenameAsync_Unsafe_Id_Is_NoOp()
    {
        var root = NewRoot();

        try
        {
            var projects = new ProjectService(new SessionStore(root));
            await projects.RenameAsync("../evil", "hacked");

            Assert.Empty(await projects.ListAsync());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task SaveAsync_Unsafe_Session_Id_Is_Rejected()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            session.SessionId = "../evil";

            await Assert.ThrowsAsync<ProjectCorruptException>(() => store.SaveAsync(session, "p"));
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task LoadAsync_Unsafe_Id_Is_Rejected()
    {
        var root = NewRoot();

        try
        {
            var store = new SessionStore(root);
            await Assert.ThrowsAsync<ProjectCorruptException>(() => store.LoadAsync("../evil"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string WriteFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
    }

    private static void Cleanup(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}