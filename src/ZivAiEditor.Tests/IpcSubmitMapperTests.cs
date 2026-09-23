using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>Step 7 Phase 2 IPC submit mapping tests (no GPU).</summary>
public class IpcSubmitMapperTests
{
    [Fact]
    public void T2I_Maps_Op_And_Leaves_Source_And_Anchor_Null()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest { Op = EditOps.T2I, ImagePath = null, Prompt = "a cat" });

        Assert.Equal("t2i", request.Op);
        Assert.Null(request.Payload.ImagePath);
        Assert.Null(request.Payload.Anchor);
    }

    [Fact]
    public void Inpaint_Maps_Op_And_Preserves_ImagePath()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest { Op = EditOps.Inpaint, ImagePath = "in.png", Prompt = "fix" });

        Assert.Equal("inpaint", request.Op);
        Assert.Equal("in.png", request.Payload.ImagePath);
        Assert.Null(request.Payload.Anchor);
    }

    [Fact]
    public void Outpaint_Maps_Anchor_And_Explicit_Resolution()
    {
        var policy = new ResolutionPolicy
        {
            Mode = ResolutionMode.Explicit,
            Width = 2752,
            Height = 1536,
            MaxPixels = 4_700_000,
        };

        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest
            {
                Op = EditOps.Outpaint,
                ImagePath = "in.png",
                Anchor = "top-left",
                Resolution = policy,
            });

        Assert.Equal("outpaint", request.Op);
        Assert.Equal("top-left", request.Payload.Anchor);
        Assert.NotNull(request.Payload.Resolution);
        Assert.Equal("explicit", request.Payload.Resolution!.Mode);
        Assert.Equal(2752, request.Payload.Resolution.Width);
        Assert.Equal(1536, request.Payload.Resolution.Height);
        Assert.Equal(4_700_000, request.Payload.Resolution.MaxPixels);
    }

    [Fact]
    public void AdditionalImages_Map_In_Order()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest
            {
                Op = EditOps.Inpaint,
                ImagePath = "main.png",
                Prompt = "use <image2> and <image3>",
                AdditionalImages = new[] { "ref1.png", "ref2.png" },
            });

        Assert.Equal(new[] { "ref1.png", "ref2.png" }, request.Payload.AdditionalImages);
    }

    [Fact]
    public void AdditionalImages_Default_To_Empty()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest { Op = EditOps.Inpaint, ImagePath = "main.png", Prompt = "x" });

        Assert.NotNull(request.Payload.AdditionalImages);
        Assert.Empty(request.Payload.AdditionalImages);
    }
}