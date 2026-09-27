using System.Text.Json;
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

    [Fact]
    public void ModelId_Maps_To_Payload()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest
            {
                Op = EditOps.Inpaint,
                ImagePath = "in.png",
                Prompt = "x",
                ModelId = "qwen-image-2.1",
            });

        Assert.Equal("qwen-image-2.1", request.Payload.ModelId);
    }

    [Fact]
    public void Loras_Map_To_Payload_In_Order()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest
            {
                Op = EditOps.Inpaint,
                ImagePath = "in.png",
                Prompt = "x",
                Loras = new List<LoraOptions>
                {
                    new() { Path = "a", StrengthModel = 0.8 },
                    new() { Path = "b" },
                },
            });

        Assert.NotNull(request.Payload.Loras);
        Assert.Equal(new[] { "a", "b" }, request.Payload.Loras!.Select(lora => lora.Path));
    }

    [Fact]
    public void Legacy_Single_Lora_Upgrades_To_Payload_Loras()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest
            {
                Op = EditOps.Inpaint,
                ImagePath = "in.png",
                Prompt = "x",
                Lora = new LoraOptions { Path = "only" },
            });

        Assert.Equal("only", Assert.Single(request.Payload.Loras!).Path);
    }

    [Fact]
    public void Duplicate_Loras_Are_Deduplicated_In_Payload()
    {
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest
            {
                Op = EditOps.Inpaint,
                ImagePath = "in.png",
                Prompt = "x",
                Loras = new List<LoraOptions>
                {
                    new() { Path = "same" },
                    new() { Path = "same" },
                },
            });

        Assert.Equal("same", Assert.Single(request.Payload.Loras!).Path);
    }

    [Fact]
    public void Inpaint_Maps_MaskPath()
    {
        // Step 9C.7-B: the mask PNG path still maps to the IPC payload; FeatherPx is a
        // C#-only MaskSpec field and is intentionally not an IPC field.
        var request = IpcSubmitMapper.BuildSubmitRequest(
            "r",
            "t",
            new EditRequest
            {
                Op = EditOps.Inpaint,
                ImagePath = "in.png",
                MaskPath = "mask.png",
                Prompt = "fix",
            });

        Assert.Equal("inpaint", request.Op);
        Assert.Equal("mask.png", request.Payload.MaskPath);
    }

    [Fact]
    public void OptimizationOptions_Serialize_To_Documented_Keys()
    {
        // Batch 2A / D4: the real IPC serializer must emit the frozen contract keys
        // "magcache" / "magcache_thresh" (NOT the SnakeCaseLower "mag_cache"). Locks the
        // C# wire format to Python handlers.py + contracts/ipc-protocol.md.
        var json = JsonSerializer.Serialize(
            new OptimizationOptions { MagCache = true, MagCacheThresh = 0.24 },
            IpcJsonContext.Default.OptimizationOptions);

        Assert.Contains("\"magcache\":true", json);
        Assert.Contains("\"magcache_thresh\":0.24", json);
        Assert.DoesNotContain("mag_cache", json);
    }
}