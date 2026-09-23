using ZivAiEditor.Agent;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9C.6-E project loader tests (JSON rebuild, version / corrupt / missing).</summary>
public class SessionLoaderTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zivai_load_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteImage(string directory, string name)
        => File.WriteAllBytes(Path.Combine(directory, name), new byte[] { 1, 2, 3 });

    private static string Node(string id, string? parent, string image, string command = "c")
        => $"{{\"node_id\": \"{id}\", \"parent_node_id\": {(parent is null ? "null" : $"\"{parent}\"")}, "
           + $"\"image_path\": \"{image}\", \"command\": \"{command}\", \"created_at\": \"2026-01-01T00:00:00+00:00\"}}";

    private static string MakeFile(string name, string current, params string[] nodes)
        => $"{{\"version\": 1, \"name\": \"{name}\", \"session_id\": \"sid\", \"current_node_id\": \"{current}\", "
           + "\"created_at\": \"2026-01-01T00:00:00+00:00\", \"nodes\": [" + string.Join(",", nodes) + "]}";

    [Fact]
    public void Loads_Root_And_Child_With_Current()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            WriteImage(dir, "b.png");
            var json = MakeFile("p", "b", Node("a", null, "a.png"), Node("b", "a", "b.png", "/去水印"));

            var result = SessionLoader.LoadFromJson(json, dir);

            Assert.Empty(result.Warnings);
            Assert.Equal("p", result.Name);
            Assert.Equal("sid", result.Session.SessionId);
            Assert.Equal("b", result.Session.CurrentNodeId);
            var history = result.Session.GetHistory();
            Assert.Equal(2, history.Count);
            Assert.Equal("a", history[0].NodeId);
            Assert.EndsWith("a.png", result.Session.RootImagePath);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Version_Mismatch_Throws_Format()
    {
        var dir = NewDir();
        try
        {
            var json = "{\"version\": 2, \"session_id\": \"s\", \"nodes\": []}";

            Assert.Throws<ProjectFormatException>(() => SessionLoader.LoadFromJson(json, dir));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Corrupt_Json_Throws_Corrupt()
    {
        var dir = NewDir();
        try
        {
            Assert.Throws<ProjectCorruptException>(() => SessionLoader.LoadFromJson("{not json", dir));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Missing_Image_Skips_Node_With_Warning()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var json = MakeFile("p", "a", Node("a", null, "a.png"), Node("b", "a", "missing.png"));

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.Equal("a", loaded.NodeId);
            Assert.Single(result.Warnings);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Missing_Crop_Image_Drops_Crop_With_Warning()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var node = "{\"node_id\": \"a\", \"parent_node_id\": null, \"image_path\": \"a.png\", "
                       + "\"command\": \"c\", \"created_at\": \"2026-01-01T00:00:00+00:00\", "
                       + "\"crop\": {\"x\": 1, \"y\": 2, \"width\": 3, \"height\": 4, \"result_image_path\": \"gone.png\"}}";
            var json = MakeFile("p", "a", node);

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.Null(loaded.Crop);
            Assert.Contains(result.Warnings, w => w.Contains("裁切", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Multiple_Roots_Are_Normalized()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            WriteImage(dir, "b.png");
            var json = MakeFile("p", "a", Node("a", null, "a.png"), Node("b", null, "b.png"));

            var result = SessionLoader.LoadFromJson(json, dir);

            var nodes = result.Session.GetHistory();
            Assert.Single(nodes, n => string.IsNullOrEmpty(n.ParentNodeId));
            Assert.Contains(result.Warnings, w => w.Contains("起始节点", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Empty_Session_Loads_Without_Nodes()
    {
        var dir = NewDir();
        try
        {
            var json = MakeFile("p", "", Array.Empty<string>());

            var result = SessionLoader.LoadFromJson(json, dir);

            Assert.Empty(result.Session.GetHistory());
            Assert.Null(result.Session.CurrentNodeId);
        }
        finally
        {
            Cleanup(dir);
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
