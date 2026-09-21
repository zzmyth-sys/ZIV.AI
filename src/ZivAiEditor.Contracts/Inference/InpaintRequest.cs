namespace ZivAiEditor.Contracts.Inference;

public sealed class InpaintRequest
{
    public string ImagePath { get; init; } = "";
    public string? MaskPath { get; init; }
    public string Prompt { get; init; } = "";
    public int Steps { get; init; } = 20;
    public long Seed { get; init; } = -1;
    public double Denoise { get; init; } = 1.0;
    public string? OutputPath { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
        = new Dictionary<string, string>();

    public LoraOptions? Lora { get; init; }
    public OptimizationOptions? Optimizations { get; init; }
}
