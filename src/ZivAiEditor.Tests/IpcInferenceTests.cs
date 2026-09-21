using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using SkiaSharp;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using Xunit;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace ZivAiEditor.Tests;

/// <summary>
/// End-to-end Step 2.3 coverage over the real IPC pipe: submit -> sampling
/// progress -> preview (<c>0x02</c>) -> result, and mask handling. These tests
/// drive the actual GPU backend, so budgets are generous.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(GpuSerialCollection.Name)]
public class IpcInferenceTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(300);

    [Fact]
    public async Task Submit_Emits_Sampling_Progress_Preview_And_Result()
    {
        var input = Path.Combine(FindRepositoryRoot(), "_test_step2", "input_test_512.png");
        Assert.True(File.Exists(input), $"Input image not found: {input}");

        var inputHash = Sha256(input);
        var output = NewTempPath("zivai_step23_out");

        var options = CreateOptions();
        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var progress = new List<InferenceProgressDetail>();
        var previews = new List<PreviewFrame>();
        InferenceResultDetail? result = null;
        client.ProgressReceived += detail => { lock (progress) { progress.Add(detail); } };
        client.PreviewReceived += frame => { lock (progress) { previews.Add(frame); } };
        client.ResultReceived += value => { lock (progress) { result = value; } };

        using var timeout = new CancellationTokenSource(Budget);
        var handle = await client.SubmitInpaintAsync(
            new InpaintRequest
            {
                ImagePath = input,
                Prompt = "make the background a snowy mountain landscape, keep the subject unchanged",
                Steps = 8,
                Seed = 42,
                Denoise = 1.0,
                OutputPath = output,
            },
            progress: null,
            timeout.Token);

        Assert.Equal(TaskStatus.Succeeded, handle.Status);

        Assert.True(File.Exists(output), $"Output PNG not found: {output}");
        var (width, height) = ReadPngSize(File.ReadAllBytes(output));
        Assert.Equal(512, width);
        Assert.Equal(512, height);

        List<InferenceProgressDetail> progressSnapshot;
        List<PreviewFrame> previewSnapshot;
        lock (progress)
        {
            progressSnapshot = progress.ToList();
            previewSnapshot = previews.ToList();
        }

        var sampling = progressSnapshot.Where(f => f.Stage == "sampling" && f.Total > 0).ToList();
        Assert.True(sampling.Count >= 3, $"expected at least 3 sampling frames, got {sampling.Count}");

        var fractions = sampling.Select(f => f.Fraction).ToList();
        for (var i = 1; i < fractions.Count; i++)
        {
            Assert.True(fractions[i] >= fractions[i - 1], "sampling fraction must be non-decreasing");
        }

        Assert.True(fractions[0] >= 0.0);
        Assert.True(fractions[^1] <= 1.0 + 1e-9);
        Assert.True(fractions[^1] > 0.5, "sampling should progress past the halfway point");

        Assert.NotEmpty(previewSnapshot);
        var frame = previewSnapshot[0];
        Assert.True(frame.JpegBytes.Length > 2, "preview frame should carry JPEG bytes");
        Assert.Equal(0xFF, frame.JpegBytes[0]);
        Assert.Equal(0xD8, frame.JpegBytes[1]);
        using (var bitmap = SKBitmap.Decode(frame.JpegBytes))
        {
            Assert.NotNull(bitmap);
            Assert.True(bitmap.Width > 0 && bitmap.Height > 0, "the preview JPEG should decode to an image");
        }

        Assert.NotNull(result);
        Assert.True(string.Equals(output, result!.OutputPath, StringComparison.OrdinalIgnoreCase),
            $"unexpected output path: {result.OutputPath}");
        Assert.True(result.DurationMs > 0, "duration_ms should be positive");
        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
        Assert.Equal(42, result.Seed);

        // Z24: the source image must be byte-for-byte unchanged.
        Assert.Equal(inputHash, Sha256(input));
    }

    [Fact]
    public async Task Submit_With_Binary_Mask_Produces_Output()
    {
        var input = Path.Combine(FindRepositoryRoot(), "_test_step2", "input_test_512.png");
        Assert.True(File.Exists(input), $"Input image not found: {input}");

        var mask = NewTempPath("zivai_step23_mask");
        CreateBinaryMask(mask, 512, 512);
        var inputHash = Sha256(input);
        var maskHash = Sha256(mask);
        var output = NewTempPath("zivai_step23_masked");

        var options = CreateOptions();
        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        using var timeout = new CancellationTokenSource(Budget);
        var handle = await client.SubmitInpaintAsync(
            new InpaintRequest
            {
                ImagePath = input,
                MaskPath = mask,
                Prompt = "replace the masked region with a red square, keep the rest unchanged",
                Steps = 8,
                Seed = 7,
                Denoise = 1.0,
                OutputPath = output,
            },
            progress: null,
            timeout.Token);

        Assert.Equal(TaskStatus.Succeeded, handle.Status);

        Assert.True(File.Exists(output), $"Output PNG not found: {output}");
        var (width, height) = ReadPngSize(File.ReadAllBytes(output));
        Assert.Equal(512, width);
        Assert.Equal(512, height);

        // Z24: neither the source image nor the mask may be modified.
        Assert.Equal(inputHash, Sha256(input));
        Assert.Equal(maskHash, Sha256(mask));
    }

    private static void CreateBinaryMask(string path, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        var pixels = bitmap.GetPixelSpan();
        pixels.Clear();
        for (var y = height / 4; y < height * 3 / 4; y++)
        {
            for (var x = width / 4; x < width * 3 / 4; x++)
            {
                pixels[(y * width) + x] = 255;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        // PNG signature (8B) + IHDR length/type (8B) => width at 16, height at 20.
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (width, height);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string NewTempPath(string prefix)
        => Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N") + ".png");

    private static PythonBackendOptions CreateOptions()
    {
        var root = FindRepositoryRoot();
        return new PythonBackendOptions
        {
            PipeName = "zivai.infer.test." + Guid.NewGuid().ToString("N"),
            PythonExe = Path.Combine(root, "Comfyui", "python_embeded", "python.exe"),
            Script = Path.Combine(root, "python", "server", "main.py"),
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, "DOC", "FROZEN.md");
            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the ZIV.AI repository root from " + AppContext.BaseDirectory);
    }
}
