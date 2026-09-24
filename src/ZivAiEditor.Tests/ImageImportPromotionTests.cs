using ZivAiEditor.UI.Editing;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 9C.10-P2 (R5): the empty-session import promotion rule. Pure, no Avalonia.</summary>
public class ImageImportPromotionTests
{
    [Fact]
    public void Promotes_When_An_Empty_Strip_Gains_Images_And_No_Root()
    {
        Assert.True(ImageImportPromotion.ShouldPromote(0, 1, false));
        Assert.True(ImageImportPromotion.ShouldPromote(0, 3, false));
    }

    [Fact]
    public void Does_Not_Promote_When_A_Root_Already_Exists()
    {
        Assert.False(ImageImportPromotion.ShouldPromote(0, 3, true));
    }

    [Fact]
    public void Does_Not_Promote_On_Removal_Append_Or_NoOp()
    {
        Assert.False(ImageImportPromotion.ShouldPromote(2, 0, false)); // removal
        Assert.False(ImageImportPromotion.ShouldPromote(1, 3, false)); // append to non-empty
        Assert.False(ImageImportPromotion.ShouldPromote(0, 0, false)); // no-op
    }
}
