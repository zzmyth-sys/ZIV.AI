using ZivAiEditor.Agent;
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

        var first = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var second = session.AppendNode(first.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.Null(first.ParentNodeId);
        Assert.Equal(first.NodeId, second.ParentNodeId);
        Assert.Equal(2, session.Nodes.Count);
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
    public void GetCurrentImagePath_Returns_Root_When_No_Nodes()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");

        Assert.Equal(@"C:\img\root.png", session.GetCurrentImagePath());
        Assert.Empty(session.GetHistory());
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
        session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        session.AppendNode(null, @"C:\img\out2.png", "cmd2");
        session.AppendNode(null, @"C:\img\out3.png", "cmd3");

        var history = session.GetHistory();

        Assert.Equal(3, history.Count);
        Assert.Contains(history, node => node.Command == "cmd1");
        Assert.Contains(history, node => node.Command == "cmd2");
        Assert.Contains(history, node => node.Command == "cmd3");
    }

    [Fact]
    public void BranchFrom_HistoricalNode_Creates_Sibling()
    {
        var session = new EditSession();
        var root = session.AppendNode(null, @"C:\img\out1.png", "cmd1");
        var siblingA = session.AppendNode(root.NodeId, @"C:\img\out2.png", "cmd2");

        Assert.True(session.NavigateTo(root.NodeId));

        var siblingB = session.AppendNode(session.CurrentNodeId, @"C:\img\out3.png", "cmd3");

        Assert.Equal(root.NodeId, siblingA.ParentNodeId);
        Assert.Equal(root.NodeId, siblingB.ParentNodeId);
        Assert.Equal(3, session.Nodes.Count);
        Assert.Equal(siblingB.NodeId, session.CurrentNodeId);
        Assert.Equal(@"C:\img\out3.png", session.GetCurrentImagePath());
    }
}
