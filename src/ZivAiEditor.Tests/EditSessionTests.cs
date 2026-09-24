using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 8 in-memory session DAG tests (no GPU).</summary>
public class EditSessionTests
{
    [Fact]
    public void AppendNode_Builds_DAG()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootNode = session.GetHistory()[0];

        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var second = session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.Equal(rootNode.NodeId, first.ParentNodeId);
        Assert.Equal(first.NodeId, second.ParentNodeId);
        Assert.Equal(3, session.Nodes.Count);
        Assert.Equal(second.NodeId, session.CurrentNodeId);
    }

    [Fact]
    public void Root_Node_Has_A_Single_Image_Pack_And_No_Used_Images()
    {
        // Step 9C.10: the root's pack is the imported image; nothing was edited yet.
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        var root = session.GetHistory()[0];

        Assert.Equal(new[] { @"C:\img\root.png" }, root.ImagePaths);
        Assert.Empty(root.UsedImagePaths);
    }

    [Fact]
    public void SetRoot_Pack_Normalizes_And_Resets_The_Session()
    {
        // Step 9C.10-P2: a multi-image root sets the whole pack (blanks dropped), the primary
        // ImagePath is the first kept image, and any pre-existing DAG is dropped (R3).
        var session = new EditSession();
        session.AppendNode(null, @"C:\img\old.png", "cmd");

        session.SetRoot(new[] { @"C:\img\a.png", "  ", @"C:\img\b.png" });

        Assert.Single(session.Nodes);
        var root = session.GetHistory()[0];
        Assert.Equal(new[] { @"C:\img\a.png", @"C:\img\b.png" }, root.ImagePaths);
        Assert.Equal(@"C:\img\a.png", root.ImagePath);
        Assert.Empty(root.UsedImagePaths);
        Assert.Null(root.ParentNodeId);
        Assert.Equal("原图", root.Command);
        Assert.Equal(root.NodeId, session.CurrentNodeId);
    }

    [Fact]
    public void SetRoot_Pack_Empty_Is_NoOp()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;

        session.SetRoot(Array.Empty<string>());
        session.SetRoot(new[] { "  ", "" });

        Assert.Single(session.Nodes);
        Assert.Equal(rootId, session.CurrentNodeId);
        Assert.Equal(@"C:\img\root.png", session.RootImagePath);
    }

    [Fact]
    public void SetRoot_Single_Still_Works()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        var root = session.GetHistory()[0];
        Assert.Equal(new[] { @"C:\img\root.png" }, root.ImagePaths);
        Assert.Empty(root.UsedImagePaths);
        Assert.Equal(@"C:\img\root.png", session.RootImagePath);
    }

    [Fact]
    public void AppendNode_Pack_Is_The_Output_And_UsedImages_Start_Empty()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        var node = session.AppendNode(null, @"C:\img\out1.png", "cmd1");

        Assert.Equal(new[] { @"C:\img\out1.png" }, node.ImagePaths);
        Assert.Empty(node.UsedImagePaths);
    }

    [Fact]
    public void SetNodeUsedImages_Records_Order_And_Preserves_Node_Properties()
    {
        // Step 9C.10: blank entries are dropped, order (main first) is kept, and the node's
        // identity / pack / command / crop are preserved.
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var node = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.SetNodeCrop(node.NodeId, new CropSpec
        {
            X = 1, Y = 2, Width = 3, Height = 4, ResultImagePath = @"C:\img\c.png",
        });

        session.SetNodeUsedImages(node.NodeId, new[] { @"C:\img\root.png", "   ", @"C:\img\ref.png" });

        var updated = session.Nodes[node.NodeId];
        Assert.Equal(new[] { @"C:\img\root.png", @"C:\img\ref.png" }, updated.UsedImagePaths);
        Assert.Equal(new[] { @"C:\img\out1.png" }, updated.ImagePaths);
        Assert.Equal("cmd1", updated.Command);
        Assert.NotNull(updated.Crop);
    }

    [Fact]
    public void SetNodeMask_Preserves_Pack_And_UsedImages()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var node = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.SetNodeUsedImages(node.NodeId, new[] { @"C:\img\root.png" });

        session.SetNodeMask(node.NodeId, new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 4, Height = 4 });

        var updated = session.Nodes[node.NodeId];
        Assert.Equal(new[] { @"C:\img\out1.png" }, updated.ImagePaths);
        Assert.Equal(new[] { @"C:\img\root.png" }, updated.UsedImagePaths);
    }

    [Fact]
    public void NavigateTo_Updates_CurrentNode()
    {
        var session = new EditSession();
        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var second = session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.True(session.NavigateTo(first.NodeId));
        Assert.Equal(first.NodeId, session.CurrentNodeId);

        Assert.False(session.NavigateTo("missing"));
        Assert.Equal(first.NodeId, session.CurrentNodeId);
    }

    [Fact]
    public void GetCurrentImagePath_Returns_Root_When_No_Edits()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Equal(@"C:\img\root.png", session.GetCurrentImagePath());
        Assert.Single(session.GetHistory());
        Assert.Equal(session.CurrentNodeId, session.GetHistory()[0].NodeId);
    }

    [Fact]
    public void GetCurrentImagePath_Returns_Null_When_Nothing_Set()
    {
        var session = new EditSession();

        Assert.Null(session.GetCurrentImagePath());
    }

    [Fact]
    public void GetHistory_Returns_All_Nodes()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.AppendNode(null, @"C:\img\out2.png", "cmd2");
        session.AppendNode(null, @"C:\img\out3.png", "cmd3");

        var history = session.GetHistory();

        Assert.Equal(4, history.Count);
        Assert.Equal("原图", history[0].Command);
        Assert.Null(history[0].ParentNodeId);
        Assert.Contains(history, node => node.Command == "cmd1");
        Assert.Contains(history, node => node.Command == "cmd2");
        Assert.Contains(history, node => node.Command == "cmd3");
    }

    [Fact]
    public void SetRoot_Then_ResetToRoot_Clears_Previous_DAG()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        Assert.Equal(2, session.Nodes.Count);

        session.SetRoot(@"C:\img\root2.png");

        Assert.Single(session.Nodes);
        Assert.Equal(@"C:\img\root2.png", session.RootImagePath);
        Assert.Equal(session.CurrentNodeId, session.GetHistory()[0].NodeId);
        Assert.DoesNotContain(session.GetHistory(), node => node.Command == "cmd1");

        session.AppendNode(null, @"C:\img\out2.png", "cmd2");
        Assert.Equal(2, session.Nodes.Count);

        session.ResetToRoot(@"C:\img\root3.png");

        Assert.Single(session.Nodes);
        Assert.Equal(@"C:\img\root3.png", session.RootImagePath);
        Assert.Equal(session.CurrentNodeId, session.GetHistory()[0].NodeId);
    }

    [Fact]
    public void AppendNode_With_Null_Parent_And_Existing_Root_Attaches_To_Root()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootNode = session.GetHistory()[0];

        var child = session.AppendNode(null, @"C:\img\out1.png", "cmd1");

        Assert.Equal(rootNode.NodeId, child.ParentNodeId);
    }

    [Fact]
    public void BranchFrom_HistoricalNode_Creates_Sibling()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootNode = session.GetHistory()[0];
        var root = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var siblingA = session.AppendNode(root.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.True(session.NavigateTo(root.NodeId));

        var siblingB = session.AppendNode(session.CurrentNodeId, @"C:\img\out3.png", "cmd3");

        Assert.Equal(rootNode.NodeId, root.ParentNodeId);
        Assert.Equal(root.NodeId, siblingA.ParentNodeId);
        Assert.Equal(root.NodeId, siblingB.ParentNodeId);
        Assert.Equal(4, session.Nodes.Count);
        Assert.Equal(siblingB.NodeId, session.CurrentNodeId);
        Assert.Equal(@"C:\img\out3.png", session.GetCurrentImagePath());
    }

    [Fact]
    public void GetParentImagePath_Is_Null_For_Root_Or_Unknown()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Null(session.GetParentImagePath(@"C:\img\root.png"));
        Assert.Null(session.GetParentImagePath(""));
        Assert.Null(session.GetParentImagePath("   "));
        Assert.Null(session.GetParentImagePath(null));
        Assert.Null(session.GetParentImagePath(@"C:\img\unknown.png"));
    }

    [Fact]
    public void GetParentImagePath_Root_Returns_Null()
    {
        // Step 9C.6: the root is a real node with no parent; opening it must disable compare.
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Equal(@"C:\img\root.png", session.RootImagePath);
        Assert.Null(session.GetParentImagePath(session.RootImagePath));
    }

    [Fact]
    public void GetParentImagePath_Returns_Root_For_Direct_Child()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        session.AppendNode(null, @"C:\img\out1.png", "cmd1");

        Assert.Equal(@"C:\img\root.png", session.GetParentImagePath(@"C:\img\out1.png"));
    }

    [Fact]
    public void GetParentImagePath_Returns_Parent_Output_For_Grandchild()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.Equal(@"C:\img\out1.png", session.GetParentImagePath(@"C:\img\out2.png"));
    }

    [Fact]
    public void GetParentImagePath_Is_Case_Insensitive()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        session.AppendNode(null, @"C:\img\out1.png", "cmd1");

        Assert.Equal(@"C:\img\root.png", session.GetParentImagePath(@"C:\IMG\OUT1.PNG"));
    }

    [Fact]
    public void GetPathToCurrent_Walks_Root_To_Current()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootNode = session.GetHistory()[0];
        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var second = session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        var path = session.GetPathToCurrent();

        Assert.Equal(3, path.Count);
        Assert.Equal(rootNode.NodeId, path[0].NodeId);
        Assert.Equal(first.NodeId, path[1].NodeId);
        Assert.Equal(second.NodeId, path[2].NodeId);
    }

    [Fact]
    public void GetPathToCurrent_Returns_Root_When_Root_Is_Current()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        var path = session.GetPathToCurrent();

        Assert.Single(path);
        Assert.Equal(session.CurrentNodeId, path[0].NodeId);
        Assert.Equal("原图", path[0].Command);
    }

    [Fact]
    public void GetPathToCurrent_Is_Empty_When_No_Current_Node()
    {
        var session = new EditSession();

        Assert.Empty(session.GetPathToCurrent());
    }

    [Fact]
    public void GetDepth_Counts_Ancestors()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootNode = session.GetHistory()[0];
        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var second = session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.Equal(0, session.GetDepth(rootNode));
        Assert.Equal(1, session.GetDepth(first));
        Assert.Equal(2, session.GetDepth(second));
        Assert.Equal(0, session.GetDepth(null));
    }

    [Fact]
    public void EditSession_And_EditNode_Implement_Contracts_Interfaces()
    {
        // Step 9C.5: the concrete types satisfy the Contracts abstractions the UI uses.
        var session = new EditSession();
        var node = session.AppendNode(null, @"C:\img\out1.png", "cmd1");

        Assert.IsAssignableFrom<IEditSession>(session);
        Assert.IsAssignableFrom<IEditSessionWriter>(session);
        Assert.IsAssignableFrom<IEditNode>(node);
    }

    [Fact]
    public void SetNodeCrop_Updates_PipelinePath_But_Not_ImagePath()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var node = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var crop = new CropSpec { X = 1, Y = 2, Width = 30, Height = 40, ResultImagePath = @"C:\img\out1_crop.png" };

        session.SetNodeCrop(node.NodeId, crop);

        var updated = session.Nodes[node.NodeId];
        Assert.Same(crop, updated.Crop);
        Assert.Equal(@"C:\img\out1.png", updated.ImagePath);              // unchanged
        Assert.Equal(@"C:\img\out1.png", session.GetCurrentImagePath());  // original semantics
        Assert.Equal(@"C:\img\out1_crop.png", session.GetCurrentPipelineImagePath());
    }

    [Fact]
    public void SetNodeCrop_On_Root_Keeps_RootImagePath()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;

        session.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\root_crop.png" });

        // The _rootNode reference must be re-pointed, or RootImagePath would go null.
        Assert.Equal(@"C:\img\root.png", session.RootImagePath);
        Assert.Equal(@"C:\img\root_crop.png", session.GetCurrentPipelineImagePath());
    }

    [Fact]
    public void SetNodeCrop_Unknown_Node_Is_NoOp()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        session.SetNodeCrop("missing", new CropSpec { Width = 10, Height = 10 });

        Assert.Single(session.Nodes);
        Assert.Equal(@"C:\img\root.png", session.GetCurrentPipelineImagePath());
    }

    [Fact]
    public void GetCurrentPipelineImagePath_Falls_Back_To_ImagePath()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Equal(@"C:\img\root.png", session.GetCurrentPipelineImagePath());
    }

    [Fact]
    public void GetCurrentPipelineImagePath_Null_When_No_Current()
    {
        var session = new EditSession();

        Assert.Null(session.GetCurrentPipelineImagePath());
    }

    [Fact]
    public void GetParentPipelineImagePath_Returns_Parent_Crop_Result()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.SetNodeCrop(first.NodeId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\out1_crop.png" });
        session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.Equal(@"C:\img\out1_crop.png", session.GetParentPipelineImagePath(@"C:\img\out2.png"));
        // The legacy method is unchanged: it still returns the parent's own output.
        Assert.Equal(@"C:\img\out1.png", session.GetParentImagePath(@"C:\img\out2.png"));
    }

    [Fact]
    public void GetParentPipelineImagePath_Root_Returns_Null()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Null(session.GetParentPipelineImagePath(@"C:\img\root.png"));
        Assert.Null(session.GetParentPipelineImagePath(null));
    }

    [Fact]
    public void GetParentPipelineImagePath_Falls_Back_When_Parent_Uncropped()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.Equal(@"C:\img\out1.png", session.GetParentPipelineImagePath(@"C:\img\out2.png"));
    }

    [Fact]
    public void GetParentPipelineImagePath_Accepts_A_Crop_Result_Path()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.SetNodeCrop(first.NodeId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\out1_crop.png" });
        var second = session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");
        session.SetNodeCrop(second.NodeId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\out2_crop.png" });

        // The chat carries a node's crop result; its parent's pipeline path must resolve.
        Assert.Equal(@"C:\img\out1_crop.png", session.GetParentPipelineImagePath(@"C:\img\out2_crop.png"));
    }

    [Fact]
    public void SetNodeCrop_Null_Clears_Crop()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        session.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" });
        Assert.Equal(@"C:\img\c.png", session.GetCurrentPipelineImagePath());

        session.SetNodeCrop(rootId, null);

        Assert.Null(session.Nodes[rootId].Crop);
        Assert.Equal(@"C:\img\root.png", session.GetCurrentPipelineImagePath());
        Assert.Equal(@"C:\img\root.png", session.RootImagePath);
    }

    [Fact]
    public void SetNodeMask_Sets_Mask_On_Node()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var mask = new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 10, Height = 20 };

        session.SetNodeMask(rootId, mask);

        Assert.Same(mask, session.Nodes[rootId].Mask);
        Assert.Same(mask, session.GetCurrentMaskSpec());
    }

    [Fact]
    public void SetNodeMask_Preserves_Crop()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var crop = new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" };
        session.SetNodeCrop(rootId, crop);

        session.SetNodeMask(rootId, new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 4, Height = 4 });

        Assert.Same(crop, session.Nodes[rootId].Crop);
    }

    [Fact]
    public void SetNodeMask_Unknown_Node_Is_NoOp()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        session.SetNodeMask("missing", new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 1, Height = 1 });

        Assert.Null(session.GetCurrentMaskSpec());
        Assert.Single(session.Nodes);
    }

    [Fact]
    public void SetNodeCrop_Same_Crop_Preserves_Mask()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        session.SetNodeCrop(rootId, new CropSpec { X = 1, Y = 2, Width = 30, Height = 40, ResultImagePath = @"C:\img\c.png" });
        var mask = new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 8, Height = 8 };
        session.SetNodeMask(rootId, mask);

        // An identical crop (fresh instance, same values) must not drop the mask.
        session.SetNodeCrop(rootId, new CropSpec { X = 1, Y = 2, Width = 30, Height = 40, ResultImagePath = @"C:\img\c.png" });

        Assert.Same(mask, session.Nodes[rootId].Mask);
    }

    [Fact]
    public void SetNodeCrop_Different_Crop_Clears_Mask()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        session.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\a.png" });
        session.SetNodeMask(rootId, new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 8, Height = 8 });

        session.SetNodeCrop(rootId, new CropSpec { Width = 20, Height = 20, ResultImagePath = @"C:\img\b.png" });

        Assert.Null(session.Nodes[rootId].Mask);
        Assert.NotNull(session.Nodes[rootId].Crop);
    }

    [Fact]
    public void SetNodeCrop_Clearing_To_Null_Clears_Mask()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        session.SetNodeCrop(rootId, new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\a.png" });
        session.SetNodeMask(rootId, new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 8, Height = 8 });

        session.SetNodeCrop(rootId, null);

        Assert.Null(session.Nodes[rootId].Mask);
    }

    [Fact]
    public void GetCurrentMaskSpec_Null_When_No_Mask()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Null(session.GetCurrentMaskSpec());

        var empty = new EditSession();
        Assert.Null(empty.GetCurrentMaskSpec());
    }

    [Fact]
    public void SetNodeRerun_Sets_Snapshot_On_Node()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var spec = new RerunSpec
        {
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1024 },
            AdditionalImages = new[] { @"C:\img\r.png" },
        };

        session.SetNodeRerun(rootId, spec);

        Assert.Same(spec, session.Nodes[rootId].Rerun);
        Assert.Equal(1024, session.Nodes[rootId].Rerun!.Resolution!.Side);
    }

    [Fact]
    public void SetNodeRerun_Preserves_Crop_And_Mask()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var crop = new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" };
        var mask = new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 8, Height = 8 };
        session.SetNodeCrop(rootId, crop);
        session.SetNodeMask(rootId, mask);

        session.SetNodeRerun(rootId, new RerunSpec { Resolution = new ResolutionPolicy() });

        Assert.Same(crop, session.Nodes[rootId].Crop);
        Assert.Same(mask, session.Nodes[rootId].Mask);
        Assert.NotNull(session.Nodes[rootId].Rerun);
    }

    [Fact]
    public void SetNodeRerun_Null_Clears_Snapshot()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        session.SetNodeRerun(rootId, new RerunSpec { Resolution = new ResolutionPolicy() });

        session.SetNodeRerun(rootId, null);

        Assert.Null(session.Nodes[rootId].Rerun);
    }

    [Fact]
    public void SetNodeRerun_Unknown_Node_Is_NoOp()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        session.SetNodeRerun("missing", new RerunSpec { Resolution = new ResolutionPolicy() });

        Assert.Null(session.Nodes[session.CurrentNodeId!].Rerun);
        Assert.Single(session.Nodes);
    }

    [Fact]
    public void ReplaceNodeImage_Changes_Image_Keeps_Identity()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var node = session.AppendNode(rootId, @"C:\img\old.png", "/去水印");
        var crop = new CropSpec { Width = 10, Height = 10, ResultImagePath = @"C:\img\c.png" };
        var mask = new MaskSpec { MaskImagePath = @"C:\img\m.png", Width = 8, Height = 8 };
        var rerun = new RerunSpec { Resolution = new ResolutionPolicy() };
        session.SetNodeCrop(node.NodeId, crop);
        session.SetNodeMask(node.NodeId, mask);
        session.SetNodeRerun(node.NodeId, rerun);

        session.ReplaceNodeImage(node.NodeId, @"C:\img\new.png");

        var updated = session.Nodes[node.NodeId];
        Assert.Equal(@"C:\img\new.png", updated.ImagePath);
        Assert.Equal(node.NodeId, updated.NodeId);
        Assert.Equal(rootId, updated.ParentNodeId);
        Assert.Equal("/去水印", updated.Command);
        Assert.Same(crop, updated.Crop);
        Assert.Same(mask, updated.Mask);
        Assert.Same(rerun, updated.Rerun);
    }

    [Fact]
    public void ReplaceNodeImage_Unknown_Node_Is_NoOp()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        session.ReplaceNodeImage("missing", @"C:\img\new.png");

        Assert.Equal(@"C:\img\root.png", session.Nodes[session.CurrentNodeId!].ImagePath);
    }

    [Fact]
    public void RemoveSubtree_Removes_Descendants_But_Keeps_Node()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var a = session.AppendNode(rootId, @"C:\img\a.png", "a");
        var b = session.AppendNode(a.NodeId, @"C:\img\b.png", "b");
        var c = session.AppendNode(b.NodeId, @"C:\img\c.png", "c");
        var d = session.AppendNode(a.NodeId, @"C:\img\d.png", "d");

        var removed = session.RemoveSubtree(a.NodeId);

        Assert.Equal(3, removed.Count);
        Assert.Contains(removed, n => n.NodeId == b.NodeId);
        Assert.Contains(removed, n => n.NodeId == c.NodeId);
        Assert.Contains(removed, n => n.NodeId == d.NodeId);
        Assert.True(session.Nodes.ContainsKey(a.NodeId));
        Assert.True(session.Nodes.ContainsKey(rootId));
        Assert.False(session.Nodes.ContainsKey(b.NodeId));
    }

    [Fact]
    public void RemoveSubtree_Keeps_Siblings()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var rootId = session.CurrentNodeId!;
        var a = session.AppendNode(rootId, @"C:\img\a.png", "a");
        var b = session.AppendNode(rootId, @"C:\img\b.png", "b");

        var removed = session.RemoveSubtree(a.NodeId);

        Assert.Empty(removed);
        Assert.True(session.Nodes.ContainsKey(a.NodeId));
        Assert.True(session.Nodes.ContainsKey(b.NodeId));
    }

    [Fact]
    public void RemoveSubtree_Unknown_Node_Returns_Empty()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Empty(session.RemoveSubtree("missing"));
        Assert.Single(session.Nodes);
    }
}
