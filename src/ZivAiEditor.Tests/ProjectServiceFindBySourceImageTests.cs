using System;
using System.IO;
using System.Threading.Tasks;
using ZivAiEditor.Agent.Project;
using ZivAiEditor.Agent.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Bridge §4.1-2: ProjectService.FindBySourceImageAsync (fake sessions dir, no GPU).</summary>
public class ProjectServiceFindBySourceImageTests
{
    private static string TempRoot()
        => Path.Combine(Path.GetTempPath(), "zivai_find_" + Guid.NewGuid().ToString("N"));

    private static string WriteImage(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    [Fact]
    public async Task Find_ReturnsNewestMatchingProject()
    {
        var root = TempRoot();
        var source = TempRoot();
        try
        {
            var store = new SessionStore(root);
            var service = new ProjectService(store);
            var sourceImage = WriteImage(source, "shared.png");

            var older = new EditSession { CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            older.SetRoot(sourceImage);
            await store.SaveAsync(older, "older");

            var newer = new EditSession { CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero) };
            newer.SetRoot(sourceImage);
            await store.SaveAsync(newer, "newer");

            var match = await service.FindBySourceImageAsync(Path.GetFullPath(sourceImage));

            Assert.NotNull(match);
            Assert.Equal(newer.SessionId, match!.SessionId);
        }
        finally
        {
            Cleanup(root, source);
        }
    }

    [Fact]
    public async Task Find_NoMatch_ReturnsNull()
    {
        var root = TempRoot();
        var source = TempRoot();
        try
        {
            var store = new SessionStore(root);
            var service = new ProjectService(store);
            var session = new EditSession();
            session.SetRoot(WriteImage(source, "a.png"));
            await store.SaveAsync(session, "a");

            var match = await service.FindBySourceImageAsync(
                Path.Combine(source, "other.png"));

            Assert.Null(match);
        }
        finally
        {
            Cleanup(root, source);
        }
    }

    private static void Cleanup(params string[] directories)
    {
        foreach (var directory in directories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
