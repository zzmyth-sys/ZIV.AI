using System.Text.Json;
using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9C.6-E project store tests (Agent layer, file IO only, no GPU).</summary>
public class SessionStoreTests
{
    private static string NewRoot()
        => Path.Combine(Path.GetTempPath(), "zivai_store_" + Guid.NewGuid().ToString("N"));

    private static string WriteFile(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        return path;
    }

    [Fact]
    public async Task Save_Then_Load_RoundTrips_Graph_Current_And_Crop()
    {
        var root = NewRoot();
        var source = NewRoot();
        var cropSessionId = Guid.NewGuid().ToString("N");

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootNode = session.GetHistory()[0];
            var node = session.AppendNode(null, WriteFile(source, "out.png"), "/去水印");

            var cropSource = WriteFile(source, "crop.png");
            session.SetNodeCrop(rootNode.NodeId, new CropSpec
            {
                X = 5, Y = 6, Width = 30, Height = 40, ResultImagePath = cropSource,
            });

            await store.SaveAsync(session, "我的项目");
            var loaded = await store.LoadAsync(session.SessionId);

            Assert.Equal("我的项目", loaded.Name);
            Assert.Empty(loaded.Warnings);
            Assert.Equal(session.SessionId, loaded.Session.SessionId);
            Assert.Equal(node.NodeId, loaded.Session.CurrentNodeId);

            var nodes = loaded.Session.GetHistory();
            Assert.Equal(2, nodes.Count);
            var loadedRoot = nodes.Single(n => n.NodeId == rootNode.NodeId);
            Assert.NotNull(loadedRoot.Crop);
            Assert.Equal(30, loadedRoot.Crop!.Width);
            Assert.True(File.Exists(loadedRoot.Crop.ResultImagePath));
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
            _ = cropSessionId;
        }
    }

    [Fact]
    public async Task Save_Then_Load_RoundTrips_Mask()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootNode = session.GetHistory()[0];

            session.SetNodeMask(rootNode.NodeId, new MaskSpec
            {
                MaskImagePath = WriteFile(source, "mask.png"),
                Width = 64,
                Height = 48,
                IsBinary = true,
                Invert = false,
            });

            await store.SaveAsync(session, "遮罩项目");
            var loaded = await store.LoadAsync(session.SessionId);

            Assert.Empty(loaded.Warnings);
            var loadedRoot = loaded.Session.GetHistory().Single(n => n.NodeId == rootNode.NodeId);
            Assert.NotNull(loadedRoot.Mask);
            Assert.Equal(64, loadedRoot.Mask!.Width);
            Assert.Equal(48, loadedRoot.Mask.Height);
            Assert.True(loadedRoot.Mask.IsBinary);
            Assert.False(loadedRoot.Mask.Invert);
            Assert.True(File.Exists(loadedRoot.Mask.MaskImagePath));
            Assert.EndsWith(rootNode.NodeId + "_mask.png", loadedRoot.Mask.MaskImagePath, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Save_Then_Load_RoundTrips_Negative_Crop_Origin()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootNode = session.GetHistory()[0];

            // Step 9C.4-B: an outpaint origin is negative and must persist as-is.
            session.SetNodeCrop(rootNode.NodeId, new CropSpec
            {
                X = -12, Y = -7, Width = 60, Height = 45,
                ResultImagePath = WriteFile(source, "crop.png"),
            });

            await store.SaveAsync(session, "外扩");
            var loaded = await store.LoadAsync(session.SessionId);

            var loadedRoot = loaded.Session.GetHistory().Single(n => n.NodeId == rootNode.NodeId);
            Assert.NotNull(loadedRoot.Crop);
            Assert.Equal(-12, loadedRoot.Crop!.X);
            Assert.Equal(-7, loadedRoot.Crop.Y);
            Assert.Equal(60, loadedRoot.Crop.Width);
            Assert.Equal(45, loadedRoot.Crop.Height);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Save_Writes_Relative_Image_Names()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootNode = session.GetHistory()[0];

            await store.SaveAsync(session, "p");

            var json = await File.ReadAllTextAsync(
                Path.Combine(store.GetProjectDirectory(session.SessionId), "session.json"));
            Assert.Contains("\"version\": 1", json);
            Assert.Contains(rootNode.NodeId + ".png", json);
            Assert.DoesNotContain(source.Replace("\\", "\\\\"), json);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Save_Does_Not_Record_Additional_Images()
    {
        // D7 (Step 9C.5-D): reference images are a per-request concern and must not
        // leak into session.json.
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));

            await store.SaveAsync(session, "p");

            var json = await File.ReadAllTextAsync(
                Path.Combine(store.GetProjectDirectory(session.SessionId), "session.json"));
            Assert.DoesNotContain("additional_images", json);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
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

            var projects = await store.ListAsync();

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
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            await store.SaveAsync(session, "p");

            await store.DeleteAsync(session.SessionId);

            Assert.False(Directory.Exists(store.GetProjectDirectory(session.SessionId)));
            Assert.Empty(await store.ListAsync());
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
            var store = new SessionStore(root);
            Assert.Null(store.GetLastProjectId());

            await store.SetLastProjectIdAsync("abc123");
            Assert.Equal("abc123", store.GetLastProjectId());

            await store.SetLastProjectIdAsync(null);
            Assert.Null(store.GetLastProjectId());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ExportTo_Writes_To_External_Directory()
    {
        var root = NewRoot();
        var source = NewRoot();
        var external = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));

            var result = await store.ExportToAsync(session, "copy", external);

            Assert.Equal(external, result);
            Assert.True(File.Exists(Path.Combine(external, "session.json")));
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
            Cleanup(external);
        }
    }

    [Fact]
    public async Task ReSave_Does_Not_Throw_On_Self_Copy()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));

            await store.SaveAsync(session, "p");
            // Load points node images back at the project dir; a second save must not
            // copy a file onto itself.
            var loaded = await store.LoadAsync(session.SessionId);
            await store.SaveAsync(loaded.Session, "p2");

            Assert.Equal("p2", (await store.ListAsync())[0].Name);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
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

            var store = new SessionStore(root);

            Assert.Empty(await store.ListAsync());
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
