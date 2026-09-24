using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Project;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Project catalog tests (module-boundary migration step 3): moved from <c>SessionStoreTests</c>,
/// which keeps only the session-content save / load / export coverage. File IO only, no GPU.
/// </summary>
public class ProjectServiceTests
{
    private static string NewRoot()
        => Path.Combine(Path.GetTempPath(), "zivai_projects_" + Guid.NewGuid().ToString("N"));

    private static string WriteFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
    }

    [Fact]
    public void SessionStore_Implements_ProjectMetadataPort()
    {
        // Z-006 closure: the session store satisfies the project-metadata port, so ProjectService
        // depends on IProjectMetadataStore instead of the concrete SessionStore.
        Assert.IsAssignableFrom<IProjectMetadataStore>(new SessionStore());
    }

    [Fact]
    public async Task List_Returns_Saved_Projects_Newest_First()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var first = new EditSession();
            first.SetRoot(WriteFile(source, "a.png"));
            await store.SaveAsync(first, "A");
            await Task.Delay(20);
            var second = new EditSession();
            second.SetRoot(WriteFile(source, "b.png"));
            await store.SaveAsync(second, "B");

            var projects = await new ProjectService(store).ListAsync();

            Assert.Equal(2, projects.Count);
            Assert.Equal("B", projects[0].Name);
            Assert.Equal("A", projects[1].Name);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Delete_Removes_Project_Directory()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var projects = new ProjectService(store);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            await store.SaveAsync(session, "p");

            await projects.DeleteAsync(session.SessionId);

            Assert.False(Directory.Exists(projects.GetDirectory(session.SessionId)));
            Assert.Empty(await projects.ListAsync());
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Last_Project_Id_RoundTrips_And_Clears()
    {
        var root = NewRoot();

        try
        {
            var projects = new ProjectService(new SessionStore(root));
            Assert.Null(projects.GetLastProjectId());

            await projects.SetLastProjectIdAsync("abc123");
            Assert.Equal("abc123", projects.GetLastProjectId());

            await projects.SetLastProjectIdAsync(null);
            Assert.Null(projects.GetLastProjectId());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task List_Skips_Unsupported_Version()
    {
        var root = NewRoot();
        var projectDir = Path.Combine(root, "old");

        try
        {
            Directory.CreateDirectory(projectDir);
            await File.WriteAllTextAsync(
                Path.Combine(projectDir, "session.json"),
                "{\"version\": 99, \"session_id\": \"old\", \"name\": \"old\"}");

            var projects = new ProjectService(new SessionStore(root));

            Assert.Empty(await projects.ListAsync());
        }
        finally
        {
            Cleanup(root);
        }
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
