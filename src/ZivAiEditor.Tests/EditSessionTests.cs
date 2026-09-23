using ZivAiEditor.Agent;
using ZivAiEditor.Contracts.Planning;
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
}
