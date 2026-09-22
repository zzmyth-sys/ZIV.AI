using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 7 EditRequest contract defaults and per-op construction (no GPU).</summary>
public class EditRequestTests
{
    [Fact]
    public void Defaults_Are_Inpaint_With_Null_Source()
    {
        var request = new EditRequest();

        Assert.Equal(EditOps.Inpaint, request.Op);
        Assert.Null(request.ImagePath);
        Assert.Null(request.MaskPath);
        Assert.Null(request.Anchor);
        Assert.Equal(25, request.Steps);
    }

    [Fact]
    public void T2I_Construction_Sets_Op_And_Null_ImagePath()
    {
        var request = new EditRequest
        {
            Op = EditOps.T2I,
            ImagePath = null,
            Prompt = "a cat",
        };

        Assert.Equal(EditOps.T2I, request.Op);
        Assert.Null(request.ImagePath);
    }
}