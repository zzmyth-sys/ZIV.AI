using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ZivAiEditor.Agent;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Imaging;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Tools;

namespace E2eStep7;

/// <summary>
/// Temporary Step 7 GPU end-to-end verifier (NOT part of the solution).
/// Runs three paths through the real stack: Executor -> Tools ->
/// IpcInferenceClient -> Python pipeline -> PNG.
///   t2i      : EditPlan with empty MainImagePath -> QW21edit -> op="t2i"
///   outpaint : QW21outpaint with anchor + Explicit resolution -> op="outpaint"
///   rgba     : t2i with an RGBA prompt -> check the PNG actually has alpha
/// Usage: e2e_step7 [t2i|outpaint|rgba|all]  (default: all)
/// </summary>
internal static class Program
{
    private const string RepoRoot = @"D:\devlop\ZIV.AI";

    private static readonly string InputImage =
        Environment.GetEnvironmentVariable("ZIV_E2E_INPUT") is { Length: > 0 } overrideInput
            ? overrideInput
            : Path.Combine(RepoRoot, @"_test_step2\user_input_1024.png");

    private static readonly string Tag = Environment.GetEnvironmentVariable("ZIV_E2E_TAG") ?? "";
    private static readonly string OutDir = Path.Combine(RepoRoot, @"_test_step2\e2e_step7");
    private static readonly string WorkDir = Path.Combine(OutDir, "work");

    private static readonly string[] AllModes = { "t2i", "outpaint", "rgba" };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var modes = args.Length == 0 || args[0].Equals("all", StringComparison.OrdinalIgnoreCase)
            ? AllModes
            : args;

        Directory.CreateDirectory(WorkDir);

        Console.WriteLine("=== Step 7 GPU E2E: Executor -> Tools -> IPC -> Python ===");
        Console.WriteLine($"Modes : {string.Join(", ", modes)}");
        Console.WriteLine($"Input : {InputImage} (exists={File.Exists(InputImage)})");
        Console.WriteLine($"OutDir: {OutDir}");
        Console.WriteLine($"GPU before: {NvidiaSmiUsedMiB():0} MiB");
        Console.WriteLine();

        if (!File.Exists(InputImage))
        {
            Console.WriteLine("FATAL: input image missing.");
            return 2;
        }

        var beforeHash = Sha256(InputImage);

        var settings = ReadBackendSettings();
        Console.WriteLine($"[settings] pipe={settings.PipeName} python={settings.PythonExe}");
        Console.WriteLine($"[settings] script={settings.Script}");
        Console.WriteLine();

        var options = new PythonBackendOptions
        {
            PipeName = settings.PipeName,
            PythonExe = settings.PythonExe,
            Script = settings.Script,
            AutoRestartEnabled = true,
        };

        using var samplerCts = new CancellationTokenSource();
        var peakVram = 0d;
        var sampler = Task.Run(async () =>
        {
            while (!samplerCts.IsCancellationRequested)
            {
                var used = NvidiaSmiUsedMiB();
                if (used > peakVram)
                {
                    peakVram = used;
                }

                try
                {
                    await Task.Delay(500, samplerCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

        var total = Stopwatch.StartNew();
        var backend = new PythonProcessManager(options);
        var client = new IpcInferenceClient(backend, ownsProcess: true);
        var exitCode = 0;

        try
        {
            var tools = new ToolRegistry();
            tools.Register(new QwenImage21EditTool(client));
            tools.Register(new QwenImage21OutpaintTool(client));
            using var queue = new ExecutionQueue();
            var executor = new Executor(tools, queue);

            foreach (var mode in modes)
            {
                try
                {
                    var ok = await RunModeAsync(executor, mode, total);
                    if (!ok)
                    {
                        exitCode = 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"MODE {mode} EXCEPTION: {ex}");
                    exitCode = 1;
                }

                Console.WriteLine();
            }
        }
        finally
        {
            client.Dispose();
            backend.Dispose();
            samplerCts.Cancel();
            try
            {
                await sampler;
            }
            catch (OperationCanceledException)
            {
            }
        }

        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine($"GPU peak (sampled) : {peakVram:0} MiB");
        Console.WriteLine($"GPU after          : {NvidiaSmiUsedMiB():0} MiB");
        var afterHash = Sha256(InputImage);
        Console.WriteLine($"Source unchanged   : {beforeHash == afterHash}");
        Console.WriteLine($"Total elapsed      : {total.Elapsed.TotalSeconds:0.00}s");
        return exitCode;
    }

    private static async Task<bool> RunModeAsync(Executor executor, string mode, Stopwatch total)
    {
        Console.WriteLine($"################ MODE: {mode} ################");

        EditPlan plan;
        switch (mode.ToLowerInvariant())
        {
            case "t2i":
                plan = BuildEditPlan(
                    prompt: "一位穿深青汉服的女性站在古代中式茶肆中读书，木桌与灯笼，写实风格",
                    mainImage: "",
                    resolution: Side(1536),
                    outputPath: Path.Combine(OutDir, "t2i_output.png"));
                break;

            case "outpaint":
                plan = BuildOutpaintPlan(
                    prompt: "扩展画布，四周补充为古代中式茶肆环境，保持画面中心的原图内容不变",
                    anchor: "center",
                    resolution: Explicit(2048, 1280),
                    outputPath: Path.Combine(OutDir, $"outpaint_output{Tag}.png"));
                break;

            case "rgba":
                plan = BuildEditPlan(
                    prompt: "一位穿深青汉服的女性，RGBA 透明背景，alpha channel，PNG with transparency，保持人物边缘清晰",
                    mainImage: "",
                    resolution: Side(1024),
                    outputPath: Path.Combine(OutDir, "rgba_output.png"));
                break;

            default:
                Console.WriteLine($"Unknown mode '{mode}'.");
                return false;
        }

        var sw = Stopwatch.StartNew();
        var progress = new InlineProgress<TaskProgress>(p =>
            Console.WriteLine(
                $"[{total.Elapsed.TotalSeconds,7:0.00}s] {p.Status,-9} step={p.StepIndex}/{p.StepCount} frac={p.Fraction:0.000} {p.Message}"));

        var state = await executor.ExecuteAsync(plan, progress);
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"--- RESULT [{mode}] ---");
        Console.WriteLine($"TaskStatus : {state.Status}");
        Console.WriteLine($"Output     : {state.OutputImagePath}");
        Console.WriteLine($"Error      : {state.ErrorMessage}");
        Console.WriteLine($"Mode time  : {sw.Elapsed.TotalSeconds:0.00}s");

        var output = state.OutputImagePath;
        if (!string.IsNullOrEmpty(output) && File.Exists(output))
        {
            var info = new FileInfo(output);
            var (w, h) = ReadPngSize(output);
            Console.WriteLine($"Output size: {info.Length} bytes, {w}x{h}");
        }
        else
        {
            Console.WriteLine("Output file: MISSING");
        }

        var succeeded = state.Status == ZivAiEditor.Contracts.Enums.TaskStatus.Succeeded;
        Console.WriteLine($"MODE {mode} => {(succeeded ? "PASS" : "FAIL")}");
        return succeeded;
    }

    private static EditPlan BuildEditPlan(string prompt, string mainImage, ResolutionPolicy resolution, string outputPath)
    {
        var step = new EditStep
        {
            Order = 1,
            ToolName = QwenImage21EditTool.ToolName,
            Parameters = new Dictionary<string, string>
            {
                ["prompt"] = prompt,
                ["steps"] = "25",
                ["seed"] = "42",
                ["denoise"] = "1.0",
                ["output_path"] = outputPath,
            },
        };

        return new EditPlan
        {
            SourcePrompt = prompt,
            MainImagePath = mainImage,
            ReferenceImagePath = null,
            Mask = null,
            Steps = new[] { step },
            Resolution = resolution,
        };
    }

    private static EditPlan BuildOutpaintPlan(string prompt, string anchor, ResolutionPolicy resolution, string outputPath)
    {
        var step = new EditStep
        {
            Order = 1,
            ToolName = QwenImage21OutpaintTool.ToolName,
            Parameters = new Dictionary<string, string>
            {
                ["prompt"] = prompt,
                ["anchor"] = anchor,
                ["steps"] = "25",
                ["seed"] = "42",
                ["denoise"] = "1.0",
                ["output_path"] = outputPath,
            },
        };

        return new EditPlan
        {
            SourcePrompt = prompt,
            MainImagePath = InputImage,
            ReferenceImagePath = null,
            Mask = null,
            Steps = new[] { step },
            Resolution = resolution,
        };
    }

    private static ResolutionPolicy Side(int side) => new()
    {
        Mode = ResolutionMode.Side,
        Side = side,
        MaxPixels = 4_700_000,
    };

    private static ResolutionPolicy Explicit(int width, int height) => new()
    {
        Mode = ResolutionMode.Explicit,
        Width = width,
        Height = height,
        MaxPixels = 4_700_000,
    };

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static double NvidiaSmiUsedMiB()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                "nvidia-smi",
                "--query-gpu=memory.used --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return -1;
            }

            var line = process.StandardOutput.ReadLine();
            process.WaitForExit(5000);
            return double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        var bytes = new byte[24];
        using var stream = File.OpenRead(path);
        var read = stream.Read(bytes, 0, bytes.Length);
        if (read < 24)
        {
            return (0, 0);
        }

        static int BeInt(byte[] b, int offset) =>
            (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];

        return (BeInt(bytes, 16), BeInt(bytes, 20));
    }

    private static BackendConfig ReadBackendSettings()
    {
        var config = new BackendConfig();
        var path = Path.Combine(RepoRoot, "settings.ini");
        if (!File.Exists(path))
        {
            return config;
        }

        var inBackend = false;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                inBackend = line.Trim('[', ']').Trim().Equals("backend", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inBackend)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key.ToLowerInvariant())
            {
                case "pipe_name":
                    config.PipeName = value;
                    break;
                case "python_exe":
                    config.PythonExe = value;
                    break;
                case "script":
                    config.Script = value;
                    break;
            }
        }

        return config;
    }

    private sealed class BackendConfig
    {
        public string PipeName { get; set; } = "zivai.infer.v1";

        public string PythonExe { get; set; } = @"D:\devlop\ZIV.AI\Comfyui\python_embeded\python.exe";

        public string Script { get; set; } = @"D:\devlop\ZIV.AI\python\server\main.py";
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public InlineProgress(Action<T> onReport)
        {
            _onReport = onReport;
        }

        public void Report(T value) => _onReport(value);
    }
}
