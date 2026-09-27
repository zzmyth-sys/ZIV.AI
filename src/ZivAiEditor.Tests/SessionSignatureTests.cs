using ZivAiEditor.Agent.Session;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Session;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Batch P1 / B13: the dirty signature must cover every user-editable node/session field, so
/// an in-place edit (command / image pack / used images / rerun snapshot / source image) is not
/// mistaken for a clean session and the close prompt can fire. The legacy portion of the
/// signature must stay byte-stable (a re-open is never falsely dirty). Pure logic, no GPU.
/// </summary>
public class SessionSignatureTests
{
    private const string LegacyMarker = "||src=";

    private static string LegacyPrefix(string signature)
    {
        var index = signature.IndexOf(LegacyMarker, StringComparison.Ordinal);
        Assert.True(index >= 0, "signature must contain the tail marker");
        return signature[..index];
    }

    private static (EditSession Session, string NodeId, string Before) Build()
    {
        var session = new EditSession();
        session.SetRoot(@"C:\img\root.png");
        var node = session.AppendNode(session.CurrentNodeId!, @"C:\img\out.png", "/去水印");
        return (session, node.NodeId, SessionSignature.Compute(session));
    }

    [Fact]
    public void Same_Content_Recomputes_Equal()
    {
        var (session, _, before) = Build();

        Assert.Equal(before, SessionSignature.Compute(session));
    }

    [Fact]
    public void Command_Change_Changes_Signature()
    {
        var (session, nodeId, before) = Build();

        session.Nodes[nodeId] = session.Nodes[nodeId] with { Command = "/换背景 园林" };
        var after = SessionSignature.Compute(session);

        Assert.NotEqual(before, after);
        Assert.Equal(LegacyPrefix(before), LegacyPrefix(after));
    }

    [Fact]
    public void ImagePaths_Change_Changes_Signature()
    {
        var (session, nodeId, before) = Build();

        session.Nodes[nodeId] = session.Nodes[nodeId] with
        {
            ImagePaths = new[] { @"C:\img\out.png", @"C:\img\out_2.png" },
        };
        var after = SessionSignature.Compute(session);

        Assert.NotEqual(before, after);
        Assert.Equal(LegacyPrefix(before), LegacyPrefix(after));
    }

    [Fact]
    public void UsedImagePaths_Change_Changes_Signature()
    {
        var (session, nodeId, before) = Build();

        session.SetNodeUsedImages(nodeId, new[] { @"C:\img\root.png", @"C:\img\ref.png" });
        var after = SessionSignature.Compute(session);

        Assert.NotEqual(before, after);
        Assert.Equal(LegacyPrefix(before), LegacyPrefix(after));
    }

    [Fact]
    public void Rerun_Null_Vs_Present_Vs_Changed_All_Differ()
    {
        var (session, nodeId, before) = Build();

        session.SetNodeRerun(nodeId, new RerunSpec
        {
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 1536 },
        });
        var withRerun = SessionSignature.Compute(session);
        Assert.NotEqual(before, withRerun);

        // A resolution dimension change must dirty (all 7 ResolutionPolicy fields covered).
        session.SetNodeRerun(nodeId, new RerunSpec
        {
            Resolution = new ResolutionPolicy { Mode = ResolutionMode.Side, Side = 2048 },
        });
        var changed = SessionSignature.Compute(session);
        Assert.NotEqual(withRerun, changed);

        session.SetNodeRerun(nodeId, null);
        Assert.Equal(before, SessionSignature.Compute(session));
    }

    [Fact]
    public void SourceImage_Change_Changes_Signature()
    {
        var (session, _, before) = Build();

        session.SourceImage = @"C:\img\source.png";
        var after = SessionSignature.Compute(session);

        Assert.NotEqual(before, after);
        Assert.Equal(LegacyPrefix(before), LegacyPrefix(after));
    }

    [Fact]
    public void ImagePath_Change_Still_Changes_Legacy()
    {
        // Control: a legacy-covered field (ImagePath) still differs in the legacy portion.
        var (session, nodeId, before) = Build();

        session.Nodes[nodeId] = session.Nodes[nodeId] with { ImagePath = @"C:\img/other.png" };
        var after = SessionSignature.Compute(session);

        Assert.NotEqual(before, after);
        Assert.NotEqual(LegacyPrefix(before), LegacyPrefix(after));
    }
}
