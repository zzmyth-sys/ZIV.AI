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
                FeatherPx = 9,
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
            Assert.Equal(9, loadedRoot.Mask.FeatherPx);
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
                Path.Combine(root, session.SessionId, "session.json"));
            Assert.Contains("\"version\": 2", json);
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
    public async Task Save_Then_Load_RoundTrips_Rerun_Snapshot()
    {
        // Step 9C.8-A: a node's re-run snapshot (UI resolution + reference images) is
        // persisted under the node's `rerun` object; reference images are copied into
        // the project's refs/ folder and stored as relative names.
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootId = session.CurrentNodeId!;
            var node = session.AppendNode(rootId, WriteFile(source, "out.png"), "/去水印");
            session.SetNodeRerun(node.NodeId, new RerunSpec
            {
                Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 },
                AdditionalImages = new[] { WriteFile(source, "ref1.png"), WriteFile(source, "ref2.png") },
            });

            await store.SaveAsync(session, "重跑项目");
            var loaded = await store.LoadAsync(session.SessionId);

            Assert.Empty(loaded.Warnings);
            var loadedNode = loaded.Session.GetHistory().Single(n => n.NodeId == node.NodeId);
            Assert.NotNull(loadedNode.Rerun);
            Assert.NotNull(loadedNode.Rerun!.Resolution);
            Assert.Equal(ResolutionMode.Side, loadedNode.Rerun.Resolution!.Mode);
            Assert.Equal(1536, loadedNode.Rerun.Resolution.Side);
            Assert.Equal(2, loadedNode.Rerun.AdditionalImages.Count);
            Assert.All(loadedNode.Rerun.AdditionalImages, path => Assert.True(File.Exists(path)));
            Assert.Contains(
                loadedNode.Rerun.AdditionalImages,
                path => path.EndsWith("_ref1.png", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Save_Without_Snapshot_Does_Not_Record_Additional_Images()
    {
        // A node with no re-run snapshot must not write an `additional_images` array,
        // so a plain project's session.json stays unchanged (Step 9C.8-A). `rerun` may
        // still serialize as null, matching the existing `crop` / `mask` nulls.
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));

            await store.SaveAsync(session, "p");

            var json = await File.ReadAllTextAsync(
                Path.Combine(root, session.SessionId, "session.json"));
            Assert.DoesNotContain("additional_images", json);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Save_Then_Load_RoundTrips_Image_Pack_And_Used_Images()
    {
        // Step 9C.10: the node pack persists as image_paths (v2); the ordered used images
        // persist as used_image_paths. An already-copied file (the root pack) is reused;
        // an otherwise-uncopied source lands under used/.
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootId = session.CurrentNodeId!;
            var node = session.AppendNode(rootId, WriteFile(source, "out.png"), "/去水印");
            session.SetNodeUsedImages(node.NodeId, new[]
            {
                session.Nodes[rootId].ImagePath,
                WriteFile(source, "ref.png"),
            });

            await store.SaveAsync(session, "图包项目");
            var loaded = await store.LoadAsync(session.SessionId);

            Assert.Empty(loaded.Warnings);
            var loadedRoot = loaded.Session.GetHistory().Single(n => n.NodeId == rootId);
            Assert.Single(loadedRoot.ImagePaths);
            Assert.EndsWith(rootId + ".png", loadedRoot.ImagePaths[0], StringComparison.Ordinal);

            var loadedNode = loaded.Session.GetHistory().Single(n => n.NodeId == node.NodeId);
            Assert.Single(loadedNode.ImagePaths);
            Assert.EndsWith(node.NodeId + ".png", loadedNode.ImagePaths[0], StringComparison.Ordinal);
            Assert.Equal(2, loadedNode.UsedImagePaths.Count);
            Assert.All(loadedNode.UsedImagePaths, path => Assert.True(File.Exists(path)));
            Assert.EndsWith(rootId + ".png", loadedNode.UsedImagePaths[0], StringComparison.Ordinal);
            Assert.EndsWith(node.NodeId + "_2.png", loadedNode.UsedImagePaths[1], StringComparison.Ordinal);
            Assert.Contains("used", loadedNode.UsedImagePaths[1], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task Save_Then_Load_RoundTrips_Multi_Image_Root_Pack()
    {
        // Step 9C.10-P2: a multi-image root persists its whole pack ({id}.png, {id}_2.png, …)
        // and restores the same count / order.
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(new[]
            {
                WriteFile(source, "a.png"),
                WriteFile(source, "b.png"),
                WriteFile(source, "c.png"),
            });
            var rootId = session.CurrentNodeId!;

            await store.SaveAsync(session, "多图原图");
            var loaded = await store.LoadAsync(session.SessionId);

            Assert.Empty(loaded.Warnings);
            var loadedRoot = loaded.Session.GetHistory().Single(n => n.NodeId == rootId);
            Assert.Equal(3, loadedRoot.ImagePaths.Count);
            Assert.All(loadedRoot.ImagePaths, path => Assert.True(File.Exists(path)));
            Assert.EndsWith(rootId + ".png", loadedRoot.ImagePaths[0], StringComparison.Ordinal);
            Assert.EndsWith(rootId + "_2.png", loadedRoot.ImagePaths[1], StringComparison.Ordinal);
            Assert.EndsWith(rootId + "_3.png", loadedRoot.ImagePaths[2], StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
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

            Assert.Equal("p2", (await new ProjectService(store).ListAsync())[0].Name);
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task DeleteNodeArtifacts_Removes_Node_Image_And_References()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootId = session.CurrentNodeId!;
            var node = session.AppendNode(rootId, WriteFile(source, "out.png"), "/去水印");
            session.SetNodeRerun(node.NodeId, new RerunSpec
            {
                Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 },
                AdditionalImages = new[] { WriteFile(source, "ref1.png") },
            });

            await store.SaveAsync(session, "p");
            var projectDir = Path.Combine(root, session.SessionId);
            Assert.True(File.Exists(Path.Combine(projectDir, node.NodeId + ".png")));
            Assert.Single(Directory.GetFiles(Path.Combine(projectDir, "refs"), node.NodeId + "_ref*"));

            store.DeleteNodeArtifacts(session.SessionId, new[] { node.NodeId }, includeReferences: true);

            Assert.False(File.Exists(Path.Combine(projectDir, node.NodeId + ".png")));
            Assert.Empty(Directory.GetFiles(Path.Combine(projectDir, "refs"), node.NodeId + "_ref*"));
            // A sibling / root artifact is untouched.
            Assert.True(File.Exists(Path.Combine(projectDir, rootId + ".png")));
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public async Task DeleteNodeArtifacts_Without_References_Keeps_Refs()
    {
        var root = NewRoot();
        var source = NewRoot();

        try
        {
            var store = new SessionStore(root);
            var session = new EditSession();
            session.SetRoot(WriteFile(source, "root.png"));
            var rootId = session.CurrentNodeId!;
            var node = session.AppendNode(rootId, WriteFile(source, "out.png"), "/去水印");
            session.SetNodeRerun(node.NodeId, new RerunSpec
            {
                Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 },
                AdditionalImages = new[] { WriteFile(source, "ref1.png") },
            });
            await store.SaveAsync(session, "p");
            var projectDir = Path.Combine(root, session.SessionId);

            store.DeleteNodeArtifacts(session.SessionId, new[] { node.NodeId }, includeReferences: false);

            Assert.False(File.Exists(Path.Combine(projectDir, node.NodeId + ".png")));
            Assert.Single(Directory.GetFiles(Path.Combine(projectDir, "refs"), node.NodeId + "_ref*"));
        }
        finally
        {
            Cleanup(root);
            Cleanup(source);
        }
    }

    [Fact]
    public void DeleteNodeArtifacts_Missing_Project_Is_NoOp()
    {
        var root = NewRoot();

        try
        {
            var store = new SessionStore(root);
            store.DeleteNodeArtifacts("missing", new[] { "n1" }, includeReferences: true);

            Assert.False(Directory.Exists(Path.Combine(root, "missing")));
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
