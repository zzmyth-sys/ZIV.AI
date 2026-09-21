using System.Diagnostics;
using System.Runtime.Versioning;
using ZivAiEditor.App;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using TaskStatus = ZivAiEditor.Contracts.Enums.TaskStatus;

namespace E2eAcceptance;

/// <summary>
/// Step 4 acceptance: one real end-to-end edit through the IPC backend.
/// Uses the existing pipeline capability only (reference-conditioned T2I for a
/// mask-less edit; see RESULT.md task 1). Not part of the solution.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string Prompt =
        "把背景替换为古代中式茶肆，画面内应有木质桌椅、悬挂的灯笼、古朴的装饰，" +
        "保留画面中的人物、衣着与前景陈设不变，去掉右上角水印";

    private static async Task<int> Main()
    {
        var root = FindRepositoryRoot();
        var testDir = Path.Combine(root, "_test_step2");
        var input = Path.Combine(testDir, "user_input_768.png");
        var output = Path.Combine(testDir, "user_output_768.png");
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        Console.WriteLine("=== ZIV.AI Step 4 E2E acceptance ===");
        Console.WriteLine($"repo_root = {root}");
        Console.WriteLine($"input     = {input} ({new FileInfo(input).Length} bytes)");
        Console.WriteLine($"output    = {output}");
        Console.WriteLine($"gpu_before_MB = {NvidiaUsedMb():F1}");

        var settings = SettingsLoader.Load(root);
        Console.WriteLine($"settings.pipe_name = {settings.PipeName}");

        var options = new PythonBackendOptions
        {
            PipeName = settings.PipeName,
            PythonExe = settings.PythonExe,
            Script = settings.Script,
            AutoRestartEnabled = true,
        };

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var sw = Stopwatch.StartNew();
        double? tLoadStart = null;
        double? tLoadDone = null;
        double? tMoving = null;
        double? tSampleStart = null;
        double? tVae = null;
        var samplingFrames = 0;
        var previewFrames = 0;
        (int W, int H)? firstPreview = null;
        object gate = new();

        client.ProgressReceived += detail =>
        {
            var ms = sw.Elapsed.TotalMilliseconds;
            lock (gate)
            {
                if (detail.Stage == "loading_model" && detail.SubStage == "dit"
                    && detail.Fraction < 0.01 && tLoadStart is null)
                {
                    tLoadStart = ms;
                }

                if (detail.Message == "model_loaded" && tLoadDone is null)
                {
                    tLoadDone = ms;
                }

                if (detail.Message == "moving_to_gpu" && tMoving is null)
                {
                    tMoving = ms;
                }

                if (detail.Stage == "sampling" && detail.Message == "sampling"
                    && detail.Total > 0 && tSampleStart is null)
                {
                    tSampleStart = ms;
                }

                if (detail.Stage == "sampling" && detail.Message == "sampling" && detail.Total > 0)
                {
                    samplingFrames++;
                    if (detail.Step % 5 == 0 || detail.Step == detail.Total)
                    {
                        Console.WriteLine(
                            $"  [progress] sampling {detail.Step}/{detail.Total} "
                            + $"fraction={detail.Fraction:F3}");
                    }
                }

                if (detail.Stage == "vae_decode" && tVae is null)
                {
                    tVae = ms;
                    Console.WriteLine("  [progress] vae_decode");
                }
            }
        };

        client.PreviewReceived += frame =>
        {
            lock (gate)
            {
                previewFrames++;
                if (firstPreview is null && TryGetJpegSize(frame.JpegBytes, out var w, out var h))
                {
                    firstPreview = (w, h);
                    Console.WriteLine($"  [preview] first frame {w}x{h} ({frame.JpegBytes.Length} bytes)");
                }
            }
        };

        InferenceResultDetail? result = null;
        client.ResultReceived += detail =>
        {
            lock (gate)
            {
                result = detail;
            }
        };

        using var peakCts = new CancellationTokenSource();
        var peakTask = PollPeakVramAsync(peakCts.Token);

        var request = new InpaintRequest
        {
            ImagePath = input,
            Prompt = Prompt,
            Steps = 20,
            Seed = 42,
            // The current pipeline does not read `denoise` (no partial-redraw
            // semantics; see RESULT.md task 1), so 1.0 is the effective value.
            Denoise = 1.0,
            OutputPath = output,
        };

        Console.WriteLine("--- submitting ---");
        InferenceTaskHandle handle;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            handle = await client.SubmitInpaintAsync(request, null, timeout.Token);
        }
        finally
        {
            peakCts.Cancel();
            sw.Stop();
        }

        var peak = await peakTask;

        lock (gate)
        {
            Console.WriteLine("--- timing ---");
            Console.WriteLine($"  model_load_ms   = {(tLoadStart.HasValue && tLoadDone.HasValue ? tLoadDone - tLoadStart : null)}");
            Console.WriteLine($"  moving_to_gpu_ms= {(tMoving.HasValue && tSampleStart.HasValue ? tSampleStart - tMoving : null)}");
            Console.WriteLine($"  sampling_ms     = {(tSampleStart.HasValue && tVae.HasValue ? tVae - tSampleStart : null)}");
            Console.WriteLine($"  vae_decode_ms   = {(tVae.HasValue ? sw.Elapsed.TotalMilliseconds - tVae : null)}");
            Console.WriteLine($"  total_ms        = {sw.Elapsed.TotalMilliseconds:F0}");
            Console.WriteLine($"  sampling_frames = {samplingFrames}");
            Console.WriteLine($"  preview_frames  = {previewFrames}");
            Console.WriteLine($"  first_preview   = {(firstPreview is null ? "n/a" : $"{firstPreview.Value.W}x{firstPreview.Value.H}")}");
            Console.WriteLine("--- result ---");
            Console.WriteLine($"  status      = {handle.Status}");
            Console.WriteLine($"  output_path = {result?.OutputPath}");
            Console.WriteLine($"  size        = {result?.Width}x{result?.Height}");
            Console.WriteLine($"  seed        = {result?.Seed}");
            Console.WriteLine($"  duration_ms = {result?.DurationMs}");
        }

        Console.WriteLine($"gpu_peak_MB   = {peak:F1}");
        Console.WriteLine($"gpu_after_MB  = {NvidiaUsedMb():F1}");
        Console.WriteLine($"output_exists = {File.Exists(output)} ({new FileInfo(output).Length} bytes)");
        Console.WriteLine(
            handle.Status == TaskStatus.Succeeded && File.Exists(output)
                ? "RESULT=OK"
                : "RESULT=FAIL");
        return handle.Status == TaskStatus.Succeeded && File.Exists(output) ? 0 : 2;
    }

    private static async Task<double> PollPeakVramAsync(CancellationToken ct)
    {
        var peak = NvidiaUsedMb();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(400, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var used = NvidiaUsedMb();
            if (used > peak)
            {
                peak = used;
            }
        }

        return peak;
    }

    private static double NvidiaUsedMb()
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--query-gpu=memory.used");
            psi.ArgumentList.Add("--format=csv,noheader,nounits");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return -1;
            }

            var text = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            var first = text.Split('\n')[0].Trim();
            return double.TryParse(first, out var value) ? value : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static bool TryGetJpegSize(byte[] data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = data[i + 1];
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                height = (data[i + 5] << 8) | data[i + 6];
                width = (data[i + 7] << 8) | data[i + 8];
                return true;
            }

            var length = (data[i + 2] << 8) | data[i + 3];
            i += 2 + length;
        }

        return false;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DOC", "FROZEN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
