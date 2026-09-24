using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Imaging;
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

    private static string NodeV2(string id, string? parent, string[] images, string[]? used = null, string command = "c")
    {
        var imagesJson = string.Join(",", images.Select(image => $"\"{image}\""));
        var usedJson = used is null
            ? ""
            : ", \"used_image_paths\": [" + string.Join(",", used.Select(image => $"\"{image}\"")) + "]";
        return $"{{\"node_id\": \"{id}\", \"parent_node_id\": {(parent is null ? "null" : $"\"{parent}\"")}, "
               + $"\"image_paths\": [{imagesJson}], \"command\": \"{command}\", "
               + "\"created_at\": \"2026-01-01T00:00:00+00:00\"" + usedJson + "}";
    }

    private static string MakeFileV2(string name, string current, params string[] nodes)
        => $"{{\"version\": 2, \"name\": \"{name}\", \"session_id\": \"sid\", \"current_node_id\": \"{current}\", "
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
    public void Loads_Without_AdditionalImages_Field()
    {
        // R1 (Step 9C.5-D): reference images are never recorded, so an existing
        // session.json (no additional_images field) must keep loading unchanged.
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var json = MakeFile("p", "a", Node("a", null, "a.png"));

            var result = SessionLoader.LoadFromJson(json, dir);

            Assert.Empty(result.Warnings);
            Assert.Equal("a", Assert.Single(result.Session.GetHistory()).NodeId);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Loads_V1_Image_Path_As_A_Single_Image_Pack()
    {
        // Step 9C.10 backward compat: a v1 node (image_path) becomes a one-element pack.
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var json = MakeFile("p", "a", Node("a", null, "a.png"));

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.Single(loaded.ImagePaths);
            Assert.Equal(loaded.ImagePath, loaded.ImagePaths[0]);
            Assert.EndsWith("a.png", loaded.ImagePath, StringComparison.Ordinal);
            Assert.Empty(loaded.UsedImagePaths);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Loads_V2_Image_Pack_And_Used_Images()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            WriteImage(dir, "a_2.png");
            WriteImage(dir, "b.png");
            var json = MakeFileV2("p", "b",
                NodeV2("a", null, new[] { "a.png", "a_2.png" }),
                NodeV2("b", "a", new[] { "b.png" }, used: new[] { "a.png", "a_2.png" }));

            var result = SessionLoader.LoadFromJson(json, dir);

            Assert.Empty(result.Warnings);
            var nodes = result.Session.GetHistory();
            Assert.Equal(2, nodes[0].ImagePaths.Count);
            Assert.EndsWith("a.png", nodes[0].ImagePaths[0], StringComparison.Ordinal);
            Assert.EndsWith("a_2.png", nodes[0].ImagePaths[1], StringComparison.Ordinal);
            Assert.Equal(2, nodes[1].UsedImagePaths.Count);
            Assert.EndsWith("a_2.png", nodes[1].UsedImagePaths[1], StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Missing_One_Pack_Image_Keeps_Node_With_Warning()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var json = MakeFileV2("p", "a", NodeV2("a", null, new[] { "a.png", "gone.png" }));

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.Single(loaded.ImagePaths);
            Assert.Single(result.Warnings);
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
            var json = "{\"version\": 3, \"session_id\": \"s\", \"nodes\": []}";

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

    [Fact]
    public void Missing_Mask_Image_Drops_Mask_With_Warning_But_Keeps_Node()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var node = "{\"node_id\": \"a\", \"parent_node_id\": null, \"image_path\": \"a.png\", "
                       + "\"command\": \"c\", \"created_at\": \"2026-01-01T00:00:00+00:00\", "
                       + "\"mask\": {\"image_path\": \"gone_mask.png\", \"width\": 10, \"height\": 20, "
                       + "\"is_binary\": true, \"invert\": false}}";
            var json = MakeFile("p", "a", node);

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.Null(loaded.Mask);
            Assert.Contains(result.Warnings, w => w.Contains("遮罩", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Json_Without_Mask_Field_Leaves_Node_Mask_Null()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var json = MakeFile("p", "a", Node("a", null, "a.png"));

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.Null(loaded.Mask);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Loads_Mask_When_File_Exists()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            WriteImage(dir, "a_mask.png");
            var node = "{\"node_id\": \"a\", \"parent_node_id\": null, \"image_path\": \"a.png\", "
                       + "\"command\": \"c\", \"created_at\": \"2026-01-01T00:00:00+00:00\", "
                       + "\"mask\": {\"image_path\": \"a_mask.png\", \"width\": 64, \"height\": 48, "
                       + "\"is_binary\": true, \"invert\": false}}";
            var json = MakeFile("p", "a", node);

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.NotNull(loaded.Mask);
            Assert.Equal(64, loaded.Mask!.Width);
            Assert.Equal(48, loaded.Mask.Height);
            Assert.True(loaded.Mask.IsBinary);
            Assert.False(loaded.Mask.Invert);
            Assert.Equal(0, loaded.Mask.FeatherPx);
            Assert.True(File.Exists(loaded.Mask.MaskImagePath));
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Loads_Mask_FeatherPx_When_Present()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            WriteImage(dir, "a_mask.png");
            var node = "{\"node_id\": \"a\", \"parent_node_id\": null, \"image_path\": \"a.png\", "
                       + "\"command\": \"c\", \"created_at\": \"2026-01-01T00:00:00+00:00\", "
                       + "\"mask\": {\"image_path\": \"a_mask.png\", \"width\": 64, \"height\": 48, "
                       + "\"is_binary\": true, \"invert\": false, \"feather_px\": 12}}";
            var json = MakeFile("p", "a", node);

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.NotNull(loaded.Mask);
            Assert.Equal(12, loaded.Mask!.FeatherPx);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Loads_Rerun_Snapshot_With_Resolution_And_Reference()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            Directory.CreateDirectory(Path.Combine(dir, "refs"));
            File.WriteAllBytes(Path.Combine(dir, "refs", "a_ref1.png"), new byte[] { 1 });
            var node = "{\"node_id\": \"a\", \"parent_node_id\": null, \"image_path\": \"a.png\", "
                       + "\"command\": \"c\", \"created_at\": \"2026-01-01T00:00:00+00:00\", "
                       + "\"rerun\": {\"resolution\": {\"mode\": \"Side\", \"side\": 1024, \"max_pixels\": 4700000}, "
                       + "\"additional_images\": [\"refs/a_ref1.png\"]}}";
            var json = MakeFile("p", "a", node);

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.NotNull(loaded.Rerun);
            Assert.NotNull(loaded.Rerun!.Resolution);
            Assert.Equal(ResolutionMode.Side, loaded.Rerun.Resolution!.Mode);
            Assert.Equal(1024, loaded.Rerun.Resolution.Side);
            Assert.Single(loaded.Rerun.AdditionalImages);
            Assert.EndsWith("a_ref1.png", loaded.Rerun.AdditionalImages[0], StringComparison.Ordinal);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Missing_Reference_Image_Drops_It_With_Warning_But_Keeps_Resolution()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var node = "{\"node_id\": \"a\", \"parent_node_id\": null, \"image_path\": \"a.png\", "
                       + "\"command\": \"c\", \"created_at\": \"2026-01-01T00:00:00+00:00\", "
                       + "\"rerun\": {\"resolution\": {\"mode\": \"Side\", \"side\": 1024}, "
                       + "\"additional_images\": [\"refs/gone.png\"]}}";
            var json = MakeFile("p", "a", node);

            var result = SessionLoader.LoadFromJson(json, dir);

            var loaded = Assert.Single(result.Session.GetHistory());
            Assert.NotNull(loaded.Rerun);
            Assert.Empty(loaded.Rerun!.AdditionalImages);
            Assert.NotNull(loaded.Rerun.Resolution);
            Assert.Contains(result.Warnings, w => w.Contains("参考图", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void Json_Without_Rerun_Field_Leaves_Node_Rerun_Null()
    {
        var dir = NewDir();
        try
        {
            WriteImage(dir, "a.png");
            var json = MakeFile("p", "a", Node("a", null, "a.png"));

            var result = SessionLoader.LoadFromJson(json, dir);

            Assert.Null(Assert.Single(result.Session.GetHistory()).Rerun);
            Assert.Empty(result.Warnings);
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
