using System.Collections.Generic;
using ZivAiEditor.UI.Chat;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Module-boundary migration step 7-D1: covers the attachment-consumption rule extracted from
/// <c>MainWindow.Send.cs</c> into the pure <see cref="ChatFlowRules.ResolveAttachmentSend"/>.
/// </summary>
public class ChatFlowRulesTests
{
    [Fact]
    public void Ready_With_No_Attachments_Yields_Empty_References()
    {
        var plan = ChatFlowRules.ResolveAttachmentSend(
            AttachmentPreparation.Ready, new[] { "a.png" }, startNewSession: false);

        Assert.False(plan.StartNewSession);
        Assert.Empty(plan.References);
        Assert.False(plan.ShowReferenceHint);
    }

    [Fact]
    public void Ready_With_Multi_Attachments_Keeps_The_Rest_As_References()
    {
        var plan = ChatFlowRules.ResolveAttachmentSend(
            AttachmentPreparation.Ready, new[] { "a.png", "b.png", "c.png" }, startNewSession: false);

        Assert.False(plan.StartNewSession);
        Assert.Equal(new[] { "b.png", "c.png" }, plan.References);
        Assert.False(plan.ShowReferenceHint);
    }

    [Fact]
    public void NeedsDecision_Reference_Choice_Keeps_All_Attachments_As_References()
    {
        var plan = ChatFlowRules.ResolveAttachmentSend(
            AttachmentPreparation.NeedsDecision, new[] { "a.png", "b.png" }, startNewSession: false);

        Assert.False(plan.StartNewSession);
        Assert.Equal(new[] { "a.png", "b.png" }, plan.References);
        Assert.False(plan.ShowReferenceHint);
    }

    [Fact]
    public void NeedsDecision_NewSession_Resets_And_Hints_When_Extras_Remain()
    {
        var plan = ChatFlowRules.ResolveAttachmentSend(
            AttachmentPreparation.NeedsDecision, new[] { "a.png", "b.png", "c.png" }, startNewSession: true);

        Assert.True(plan.StartNewSession);
        Assert.Equal(new[] { "b.png", "c.png" }, plan.References);
        Assert.True(plan.ShowReferenceHint);
    }

    [Fact]
    public void NeedsDecision_NewSession_With_Single_Attachment_Has_No_References()
    {
        var plan = ChatFlowRules.ResolveAttachmentSend(
            AttachmentPreparation.NeedsDecision, new[] { "a.png" }, startNewSession: true);

        Assert.True(plan.StartNewSession);
        Assert.Empty(plan.References);
        Assert.False(plan.ShowReferenceHint);
    }

    [Fact]
    public void NoImage_Yields_Empty_Plan()
    {
        var plan = ChatFlowRules.ResolveAttachmentSend(
            AttachmentPreparation.NoImage, System.Array.Empty<string>(), startNewSession: false);

        Assert.False(plan.StartNewSession);
        Assert.Empty(plan.References);
        Assert.False(plan.ShowReferenceHint);
    }

    [Fact]
    public void ResolveAttachmentSend_Reference_IsSnapshot_NotLiveReference()
    {
        var source = new List<string> { "a.png", "b.png" };
        var plan = ChatFlowRules.ResolveAttachmentSend(
            AttachmentPreparation.NeedsDecision, source, startNewSession: false);
        source.Clear();
        Assert.Equal(new[] { "a.png", "b.png" }, plan.References);
    }
}
