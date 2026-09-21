using System.Runtime.Versioning;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Inference;
using Xunit;

namespace ZivAiEditor.Tests;

[SupportedOSPlatform("windows")]
[Collection(GpuSerialCollection.Name)]
public class IpcModelLoadTests
{
    // Cold load measures ~32 s on the RTX 4080 (DiT ~24 s, TE ~8 s, VAE ~8 s
    // including `import comfy`); allow a generous budget.
    private static readonly TimeSpan LoadBudget = TimeSpan.FromSeconds(180);

    [Fact]
    public async Task Submit_Triggers_Lazy_Load_Then_Second_Submit_Does_Not_Reload()
    {
        var options = CreateOptions();
        Assert.True(File.Exists(options.PythonExe), $"Python executable not found: {options.PythonExe}");
        Assert.True(File.Exists(options.Script), $"Python script not found: {options.Script}");

        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var frames = new List<InferenceProgressDetail>();
        client.ProgressReceived += detail =>
        {
            lock (frames)
            {
                frames.Add(detail);
            }
        };

        using var timeout = new CancellationTokenSource(LoadBudget);

        var cold = await client.CheckHealthAsync(timeout.Token);
        Assert.Equal("not_loaded", cold.ModelStatus);

        var firstHandle = await client.SubmitInpaintAsync(CreateRequest(), progress: null, timeout.Token);
        Assert.False(string.IsNullOrEmpty(firstHandle.TaskId));

        var firstPass = Snapshot(frames);
        var subStages = firstPass
            .Where(f => f.Stage == "loading_model")
            .Select(f => f.SubStage)
            .Where(f => f is not null)
            .Distinct()
            .ToList();
        Assert.Contains("dit", subStages);
        Assert.Contains("te", subStages);
        Assert.Contains("vae", subStages);
        Assert.Contains(firstPass, f => f.Stage == "loading_model" && f.SubStage == "dit" && f.Fraction < 0.01);
        Assert.Contains(firstPass, f => f.Fraction >= 0.99);
        Assert.Contains(firstPass, f => f.Stage == "sampling");

        var warm = await client.CheckHealthAsync(timeout.Token);
        Assert.Equal("loaded", warm.ModelStatus);
        Assert.True(warm.Models.Count > 0 && warm.Models[0].Loaded, "the model list should report loaded.");

        lock (frames)
        {
            frames.Clear();
        }

        await client.SubmitInpaintAsync(CreateRequest(), progress: null, timeout.Token);
        var secondPass = Snapshot(frames);
        Assert.DoesNotContain(secondPass, f => f.Stage == "loading_model");

        var stillLoaded = await client.CheckHealthAsync(timeout.Token);
        Assert.Equal("loaded", stillLoaded.ModelStatus);
        Assert.True(manager.IsProcessRunning, "the Python backend should still be running.");
    }

    [Fact]
    public async Task Ping_Reports_Loaded_After_First_Submit_Loads_Model()
    {
        var options = CreateOptions();
        await using var manager = new PythonProcessManager(options);
        using var client = new IpcInferenceClient(manager);

        var reports = new List<InferenceProgress>();
        var progress = new SyncProgress<InferenceProgress>(value =>
        {
            lock (reports)
            {
                reports.Add(value);
            }
        });

        using var timeout = new CancellationTokenSource(LoadBudget);

        var handle = await client.SubmitInpaintAsync(CreateRequest(), progress, timeout.Token);
        Assert.False(string.IsNullOrEmpty(handle.TaskId));

        List<InferenceProgress> snapshot;
        lock (reports)
        {
            snapshot = reports.ToList();
        }

        Assert.True(snapshot.Count >= 3, $"expected at least 3 progress reports, got {snapshot.Count}");
        Assert.Contains(snapshot, r => r.Message == "loading_model:dit");
        Assert.Contains(snapshot, r => r.Message == "loading_model:te");
        Assert.Contains(snapshot, r => r.Message == "loading_model:vae");

        var health = await client.CheckHealthAsync(timeout.Token);
        Assert.Equal("loaded", health.ModelStatus);
    }

    private static List<InferenceProgressDetail> Snapshot(List<InferenceProgressDetail> frames)
    {
        lock (frames)
        {
            return frames.ToList();
        }
    }

    private static InpaintRequest CreateRequest()
        => new()
        {
            ImagePath = Path.Combine(FindRepositoryRoot(), "_test_step2", "input_test_512.png"),
            Prompt = "step 2.2 model load test",
            Steps = 20,
            Seed = 42,
            Denoise = 1.0,
            OutputPath = Path.Combine(
                Path.GetTempPath(),
                "zivai_step22_out_" + Guid.NewGuid().ToString("N") + ".png"),
        };

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

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public SyncProgress(Action<T> report) => _report = report;

        public void Report(T value) => _report(value);
    }
}
