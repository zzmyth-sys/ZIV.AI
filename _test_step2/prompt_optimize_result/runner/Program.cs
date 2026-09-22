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

namespace PromptOptimizeRunner;

/// <summary>
/// Temporary QW21edit runner for prompt-optimization comparison (NOT part of the
/// solution). Args: &lt;promptFile&gt; &lt;outputPath&gt;. Reuses the real
/// Executor -> QW21edit -> IPC -> Python chain; passes output_path explicitly.
/// </summary>
internal static class Program
{
    private const string RepoRoot = @"D:\devlop\ZIV.AI";
    private static readonly string InputImage = Path.Combine(RepoRoot, @"_test_step2\user_input_1024.png");

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.WriteLine("usage: po_runner <promptFile> <outputPath>");
            return 2;
        }

        var promptFile = args[0];
        var outputPath = Path.GetFullPath(args[1]);
        var prompt = File.ReadAllText(promptFile, Encoding.UTF8).Trim();

        Console.WriteLine($"promptFile : {promptFile}");
        Console.WriteLine($"promptLen  : {prompt.Length}");
        Console.WriteLine($"output     : {outputPath}");
        Console.WriteLine($"GPU before : {NvidiaSmiUsedMiB():0} MiB");

        var settings = ReadBackendSettings();
        var options = new PythonBackendOptions
        {
            PipeName = settings.PipeName,
            PythonExe = settings.PythonExe,
            Script = settings.Script,
            AutoRestartEnabled = true,
        };

        using var samplerCts = new CancellationTokenSource();
        var peak = 0d;
        var sampler = Task.Run(async () =>
        {
            while (!samplerCts.IsCancellationRequested)
            {
                var used = NvidiaSmiUsedMiB();
                if (used > peak) peak = used;
                try { await Task.Delay(500, samplerCts.Token); }
                catch (OperationCanceledException) { break; }
            }
        });

        var total = Stopwatch.StartNew();
        var backend = new PythonProcessManager(options);
        var client = new IpcInferenceClient(backend, ownsProcess: true);
        var exit = 0;

        try
        {
            var tools = new ToolRegistry();
            tools.Register(new QwenImage21EditTool(client));
            using var queue = new ExecutionQueue();
            var executor = new Executor(tools, queue);

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
                    ["output_path"] = outputPath,
                },
            };

            var plan = new EditPlan
            {
                SourcePrompt = prompt,
                MainImagePath = InputImage,
                Steps = new[] { step },
            };

            var progress = new InlineProgress<TaskProgress>(p =>
                Console.WriteLine($"[{total.Elapsed.TotalSeconds,7:0.00}s] {p.Status,-9} frac={p.Fraction:0.000} {p.Message}"));

            var state = await executor.ExecuteAsync(plan, progress);

            Console.WriteLine();
            Console.WriteLine($"TaskStatus : {state.Status}");
            Console.WriteLine($"Output     : {state.OutputImagePath}");
            Console.WriteLine($"Error      : {state.ErrorMessage}");
            foreach (var ss in state.StepStates)
            {
                Console.WriteLine($"  Step {ss.StepId}: {ss.Status} dur={ss.Duration.TotalSeconds:0.00}s out={ss.OutputImagePath}");
            }

            if (!string.IsNullOrEmpty(state.OutputImagePath) && File.Exists(state.OutputImagePath))
            {
                var info = new FileInfo(state.OutputImagePath);
                var (w, h) = ReadPngSize(state.OutputImagePath);
                Console.WriteLine($"Output exists : True  {info.Length} bytes  {w}x{h}");
                Console.WriteLine($"Output SHA256 : {Sha256(state.OutputImagePath)}");
            }
            else
            {
                Console.WriteLine("Output exists : False");
                exit = 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION:");
            Console.WriteLine(ex.ToString());
            exit = 1;
        }
        finally
        {
            client.Dispose();
            backend.Dispose();
            samplerCts.Cancel();
            try { await sampler; } catch (OperationCanceledException) { }
        }

        Console.WriteLine();
        Console.WriteLine($"GPU peak   : {peak:0} MiB");
        Console.WriteLine($"GPU after  : {NvidiaSmiUsedMiB():0} MiB");
        Console.WriteLine($"Total      : {total.Elapsed.TotalSeconds:0.00}s");
        return exit;
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
                "nvidia-smi", "--query-gpu=memory.used --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return -1;
            var line = process.StandardOutput.ReadLine();
            process.WaitForExit(5000);
            return double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1;
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
        if (stream.Read(bytes, 0, bytes.Length) < 24) return (0, 0);
        static int Be(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        return (Be(bytes, 16), Be(bytes, 20));
    }

    private static BackendConfig ReadBackendSettings()
    {
        var config = new BackendConfig();
        var path = Path.Combine(RepoRoot, "settings.ini");
        if (!File.Exists(path)) return config;
        var inBackend = false;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[')
            {
                inBackend = line.Trim('[', ']').Trim().Equals("backend", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inBackend) continue;
            var sep = line.IndexOf('=');
            if (sep <= 0) continue;
            var key = line[..sep].Trim().ToLowerInvariant();
            var value = line[(sep + 1)..].Trim();
            if (key == "pipe_name") config.PipeName = value;
            else if (key == "python_exe") config.PythonExe = value;
            else if (key == "script") config.Script = value;
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
        public InlineProgress(Action<T> onReport) => _onReport = onReport;
        public void Report(T value) => _onReport(value);
    }
}
