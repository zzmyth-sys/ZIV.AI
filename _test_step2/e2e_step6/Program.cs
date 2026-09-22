using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ZivAiEditor.Agent;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Planning;
using ZivAiEditor.Contracts.Tools;
using ZivAiEditor.Tools;

namespace E2eStep6;

/// <summary>
/// Temporary Step 6 GPU end-to-end verifier (NOT part of the solution).
/// Command-style input -> hand-built EditPlan -> Executor -> QW21edit ->
/// IpcInferenceClient -> Python pipeline -> PNG.
/// </summary>
internal static class Program
{
    private const string RepoRoot = @"D:\devlop\ZIV.AI";

    private static readonly string InputImage = Path.Combine(RepoRoot, @"_test_step2\user_input_1024.png");
    private static readonly string WorkDir = Path.Combine(RepoRoot, @"_test_step2\e2e_step6\work");
    private static readonly string SourceDir = Path.GetDirectoryName(InputImage)!;

    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Directory.CreateDirectory(WorkDir);

        Console.WriteLine("=== Step 6 GPU E2E: Executor -> QW21edit -> IPC -> Python ===");
        Console.WriteLine($"Input : {InputImage} (exists={File.Exists(InputImage)})");
        Console.WriteLine($"Work  : {WorkDir}");

        if (!File.Exists(InputImage))
        {
            Console.WriteLine("FATAL: input image missing.");
            return 2;
        }

        var beforeHash = Sha256(InputImage);
        Console.WriteLine($"Input SHA256 (before): {beforeHash}");
        Console.WriteLine($"GPU before           : {NvidiaSmiUsedMiB():0} MiB");
        Console.WriteLine();

        var settings = ReadBackendSettings();
        Console.WriteLine($"[settings] pipe={settings.PipeName}");
        Console.WriteLine($"[settings] python={settings.PythonExe}");
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
            using var queue = new ExecutionQueue();
            var executor = new Executor(tools, queue);

            const string prompt = "把背景替换为古代中式茶肆，保留人物与前景";
            var step = new EditStep
            {
                Order = 1,
                ToolName = "QW21edit",
                Parameters = new Dictionary<string, string>
                {
                    ["prompt"] = prompt,
                    ["steps"] = "25",
                    ["seed"] = "42",
                    ["denoise"] = "1.0",
                },
            };

            var plan = new EditPlan
            {
                SourcePrompt = prompt,
                MainImagePath = InputImage,
                ReferenceImagePath = null,
                Mask = null,
                Steps = new[] { step },
            };

            var progress = new InlineProgress<TaskProgress>(p =>
                Console.WriteLine(
                    $"[{total.Elapsed.TotalSeconds,7:0.00}s] {p.Status,-9} step={p.StepIndex}/{p.StepCount} frac={p.Fraction:0.000} {p.Message}"));

            var state = await executor.ExecuteAsync(plan, progress);

            Console.WriteLine();
            Console.WriteLine("=== RESULT ===");
            Console.WriteLine($"TaskStatus : {state.Status}");
            Console.WriteLine($"TaskId     : {state.TaskId}");
            Console.WriteLine($"Output     : {state.OutputImagePath}");
            Console.WriteLine($"Error      : {state.ErrorMessage}");
            foreach (var ss in state.StepStates)
            {
                Console.WriteLine(
                    $"  Step {ss.StepId}: {ss.Status} dur={ss.Duration.TotalSeconds:0.00}s out={ss.OutputImagePath} err={ss.ErrorMessage}");
            }

            var output = state.OutputImagePath;
            if (!string.IsNullOrEmpty(output))
            {
                var exists = File.Exists(output);
                Console.WriteLine($"Output exists : {exists}");
                if (exists)
                {
                    var info = new FileInfo(output);
                    var (w, h) = ReadPngSize(output);
                    Console.WriteLine($"Output size   : {info.Length} bytes, {w}x{h}");
                    Console.WriteLine($"Output SHA256 : {Sha256(output)}");
                }
            }

            // output_path respect: any backend-generated default next to the source?
            Console.WriteLine();
            Console.WriteLine("=== output_path respect ===");
            Console.WriteLine($"work dir files : {string.Join(", ", Directory.GetFiles(WorkDir).Select(Path.GetFileName))}");
            var strayDefaults = Directory.GetFiles(SourceDir, "user_input_1024_ai_*.png");
            Console.WriteLine($"stray _ai_ files next to source: {(strayDefaults.Length == 0 ? "(none)" : string.Join(", ", strayDefaults))}");

            if (state.Status != ZivAiEditor.Contracts.Enums.TaskStatus.Succeeded)
            {
                exitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION:");
            Console.WriteLine(ex.ToString());
            exitCode = 1;
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

        Console.WriteLine();
        Console.WriteLine("=== SUMMARY ===");
        Console.WriteLine($"GPU peak (sampled) : {peakVram:0} MiB");
        Console.WriteLine($"GPU after          : {NvidiaSmiUsedMiB():0} MiB");
        var afterHash = Sha256(InputImage);
        Console.WriteLine($"Input SHA256 (after): {afterHash}");
        Console.WriteLine($"Source unchanged   : {beforeHash == afterHash}");
        Console.WriteLine($"Total elapsed      : {total.Elapsed.TotalSeconds:0.00}s");
        return exitCode;
    }

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
