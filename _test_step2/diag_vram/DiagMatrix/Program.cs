using System.Runtime.Versioning;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Inference;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace DiagMatrix;

/// <summary>
/// GPU matrix runner (NOT part of the solution): one submit for a given scenario,
/// recording the Python-side VRAM waveform via ZIV_AI_DIAG.
/// Scenarios: <c>t2i</c> (cold = fresh load, hot = warmup + measured) and the
/// single-run <c>inpaint</c> / <c>multi4</c> / <c>outpaint</c> cases.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var scenario = GetString(args, "--scenario", "t2i");
        var size = GetInt(args, "--size", 1536);
        var mode = GetString(args, "--mode", "cold");
        var runId = GetString(args, "--run-id", $"{scenario}_{size}_{mode}");
        var repo = Environment.GetEnvironmentVariable("ZIV_DIAG_REPO") ?? @"D:\devlop\ZIV.AI";
        var python = Environment.GetEnvironmentVariable("ZIV_DIAG_PYTHON")
            ?? Path.Combine(repo, @"Comfyui\python_embeded\python.exe");
        var script = Path.Combine(repo, @"python\server\main.py");
        var outDir = Path.Combine(repo, @"_test_step2\diag_vram");
        var assetsDir = Path.Combine(outDir, "assets");
        Directory.CreateDirectory(outDir);

        Console.WriteLine($"[diag] scenario={scenario} size={size} mode={mode} run={runId}");
        if (!string.Equals(scenario, "t2i", StringComparison.OrdinalIgnoreCase)
            && !Directory.Exists(assetsDir))
        {
            Console.WriteLine($"[diag] FATAL: assets missing at {assetsDir}; run make_assets.py first.");
            return 2;
        }

        var options = new PythonBackendOptions
        {
            PipeName = "zivai.diag." + Guid.NewGuid().ToString("N"),
            PythonExe = python,
            Script = script,
            AutoRestartEnabled = false,
            Environment = new Dictionary<string, string>
            {
                ["ZIV_AI_DIAG"] = "1",
                ["ZIV_AI_DIAG_DIR"] = outDir,
                ["ZIV_AI_DIAG_RUN_ID"] = runId,
            },
        };

        await using var backend = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(backend, ownsProcess: true);
        client.ResultReceived += result =>
            Console.WriteLine($"[result] {result.OutputPath} {result.Width}x{result.Height} {result.DurationMs}ms");

        if (!string.Equals(scenario, "t2i", StringComparison.OrdinalIgnoreCase))
        {
            var handle = await SubmitAsync(client, BuildRequest(scenario, size, outDir, assetsDir, runId, "measured"));
            return handle.Status == TaskStatus.Succeeded ? 0 : 1;
        }

        if (string.Equals(mode, "hot", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[hot] warmup submit (loads the model)");
            await SubmitAsync(client, BuildRequest(scenario, size, outDir, assetsDir, runId, "warmup"));
        }

        var final = await SubmitAsync(client, BuildRequest(scenario, size, outDir, assetsDir, runId, "measured"));
        return final.Status == TaskStatus.Succeeded ? 0 : 1;
    }

    private static EditRequest BuildRequest(
        string scenario,
        int size,
        string outDir,
        string assetsDir,
        string runId,
        string tag)
    {
        var output = Path.Combine(outDir, $"out_{runId}_{tag}.png");
        switch (scenario.ToLowerInvariant())
        {
            case "inpaint":
                return new EditRequest
                {
                    Op = EditOps.Inpaint,
                    ImagePath = Path.Combine(assetsDir, "src_1536.png"),
                    MaskPath = Path.Combine(assetsDir, "mask_1536.png"),
                    Prompt = "Replace the masked central area with an ancient Chinese tea house interior; keep the outer image unchanged.",
                    Steps = 20,
                    Seed = 42,
                    Denoise = 1.0,
                    Resolution = Side(1536),
                    OutputPath = output,
                };

            case "multi4":
                return new EditRequest
                {
                    Op = EditOps.Inpaint,
                    ImagePath = Path.Combine(assetsDir, "src_1536.png"),
                    AdditionalImages = new[]
                    {
                        Path.Combine(assetsDir, "ref2_1024.png"),
                        Path.Combine(assetsDir, "ref3_1024.png"),
                        Path.Combine(assetsDir, "ref4_1024.png"),
                    },
                    Prompt = "Compose <image1> together with the references <image2> <image3> <image4> into one coherent scene.",
                    Steps = 20,
                    Seed = 42,
                    Denoise = 1.0,
                    Resolution = Side(1536),
                    OutputPath = output,
                };

            case "outpaint":
                return new EditRequest
                {
                    Op = EditOps.Outpaint,
                    ImagePath = Path.Combine(assetsDir, "src_1536.png"),
                    Anchor = "center",
                    Prompt = "Extend the canvas around the source into an ancient Chinese tea house; keep the centered original content unchanged.",
                    Steps = 20,
                    Seed = 42,
                    Denoise = 1.0,
                    Resolution = Explicit(2304, 2304),
                    OutputPath = output,
                };

            default:
                return new EditRequest
                {
                    Op = EditOps.T2I,
                    Prompt = "A serene snowy mountain landscape at sunrise, highly detailed.",
                    Steps = 20,
                    Seed = 42,
                    Denoise = 1.0,
                    Resolution = Side(size),
                    OutputPath = output,
                };
        }
    }

    private static async Task<InferenceTaskHandle> SubmitAsync(IpcInferenceClient client, EditRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var handle = await client.SubmitEditAsync(request, progress: null, CancellationToken.None);
        Console.WriteLine($"[measured] {handle.Status} {stopwatch.Elapsed.TotalSeconds:F1}s");
        return handle;
    }

    private static ResolutionPolicy Side(int side) => new()
    {
        Mode = ResolutionMode.Side,
        Side = side,
    };

    private static ResolutionPolicy Explicit(int width, int height) => new()
    {
        Mode = ResolutionMode.Explicit,
        Width = width,
        Height = height,
        MaxPixels = 6_000_000,
    };

    private static int GetInt(string[] args, string name, int fallback)
        => int.TryParse(GetString(args, name, null), out var value) ? value : fallback;

    private static string GetString(string[] args, string name, string? fallback)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return fallback ?? throw new ArgumentException($"{name} is required.");
    }
}
