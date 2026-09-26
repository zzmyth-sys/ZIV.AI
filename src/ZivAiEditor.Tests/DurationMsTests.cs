using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9C.21 per-node execution duration (Agent layer, file IO only, no GPU).</summary>
public class DurationMsTests
{
    private static string NewRoot()
        => Path.Combine(Path.GetTempPath(), "zivai_dur_" + Guid.NewGuid().ToString("N"));

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
            Directory.Delete(directory, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void SetNodeDurationMs_Sets_And_Preserves_Other_Fields()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var node = session.AppendNode(null, @"C:\img\out.png", "/inpaint");
        var crop = new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" };
        session.SetNodeCrop(node.NodeId, crop);
        var mask = new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 8, Height = 8 };
        session.SetNodeMask(node.NodeId, mask);
        var rerun = new RerunSpec { Resolution = new ResolutionPolicy() };
        session.SetNodeRerun(node.NodeId, rerun);

        session.SetNodeDurationMs(node.NodeId, 12345);

        var updated = session.Nodes[node.NodeId];
        Assert.Equal(12345, updated.DurationMs);
        Assert.Equal(node.NodeId, updated.NodeId);
        Assert.Equal(node.ImagePath, updated.ImagePath);
        Assert.Same(crop, updated.Crop);
        Assert.Same(mask, updated.Mask);
        Assert.Same(rerun, updated.Rerun);
    }

    [Fact]
    public void SetNodeDurationMs_Unknown_Node_Is_NoOp()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var before = session.Nodes.Count;

        session.SetNodeDurationMs("missing", 999);

        Assert.Equal(before, session.Nodes.Count);
    }

    [Fact]
    public void Rebuild_Sites_Preserve_DurationMs()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var node = session.AppendNode(null, @"C:\img\out.png", "/inpaint");
        session.SetNodeDurationMs(node.NodeId, 5000);

        session.SetNodeCrop(node.NodeId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" });
        Assert.Equal(5000, session.Nodes[node.NodeId].DurationMs);

        session.SetNodeMask(node.NodeId, new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 4, Height = 4 });
        Assert.Equal(5000, session.Nodes[node.NodeId].DurationMs);

        session.SetNodeRerun(node.NodeId, new RerunSpec { Resolution = new ResolutionPolicy() });
        Assert.Equal(5000, session.Nodes[node.NodeId].DurationMs);

        session.SetNodeUsedImages(node.NodeId, new[] { @"C:\img\a.png" });
        Assert.Equal(5000, session.Nodes[node.NodeId].DurationMs);

        session.ReplaceNodeImage(node.NodeId, @"C:\img\new.png");
        Assert.Equal(5000, session.Nodes[node.NodeId].DurationMs);
    }

    [Fact]
    public async Task Save_Then_Load_RoundTrips_DurationMs()
    {
        var root = NewRoot();
        var source = NewRoot();
        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var node = session.AppendNode(null, WriteFile(source, "out.png"), "/inpaint");
            session.SetNodeDurationMs(node.NodeId, 4200);

            await store.SaveAsync(session, "dur");
            var loaded = await store.LoadAsync(session.SessionId);

            var loadedNode = loaded.Session.GetHistory().Single(n => n.NodeId == node.NodeId);
            Assert.Equal(4200, loadedNode.DurationMs);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Legacy_Node_Without_Duration_Loads_Null()
    {
        var root = NewRoot();
        var source = NewRoot();
        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var node = session.AppendNode(null, WriteFile(source, "out.png"), "/inpaint");

            await store.SaveAsync(session, "legacy");
            var json = await File.ReadAllTextAsync(Path.Combine(root, session.SessionId, "session.json"));
            Assert.DoesNotContain("duration_ms", json);

            var loaded = await store.LoadAsync(session.SessionId);
            Assert.Null(loaded.Session.GetHistory().Single(n => n.NodeId == node.NodeId).DurationMs);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task NormalizeRoots_Preserves_DurationMs_On_Reparented_Root()
    {
        var root = NewRoot();
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.Combine(root, id);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "a.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(directory, "b.png"), new byte[] { 1 });
            var json =
                "{\"version\":2,\"name\":\"x\",\"session_id\":\"" + id + "\"," +
                "\"created_at\":\"2026-01-01T00:00:00+00:00\",\"nodes\":[" +
                "{\"node_id\":\"a\",\"parent_node_id\":null,\"image_paths\":[\"a.png\"],\"command\":\"原图\",\"created_at\":\"2026-01-01T00:00:00+00:00\"}," +
                "{\"node_id\":\"b\",\"parent_node_id\":null,\"image_paths\":[\"b.png\"],\"command\":\"/x\",\"created_at\":\"2026-01-01T00:00:01+00:00\",\"duration_ms\":777}]}";
            await File.WriteAllTextAsync(Path.Combine(directory, "session.json"), json);

            var store = new SessionStore(root);
            var loaded = await store.LoadAsync(id);

            var b = loaded.Session.GetHistory().Single(n => n.NodeId == "b");
            Assert.NotNull(b.ParentNodeId);
            Assert.Equal(777, b.DurationMs);
        }
        finally
        {
            Cleanup(root);
        }
    }
}
