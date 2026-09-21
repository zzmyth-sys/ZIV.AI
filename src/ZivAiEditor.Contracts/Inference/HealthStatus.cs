namespace ZivAiEditor.Contracts.Inference;

public sealed class HealthStatus
{
    public string Status { get; init; } = "down";
    public string? Version { get; init; }
    public string ModelStatus { get; init; } = "not_loaded";
    public double VramUsedMb { get; init; }
    public IReadOnlyList<ModelStatus> Models { get; init; } = Array.Empty<ModelStatus>();
    public int IdleUnloadSeconds { get; init; }
}

public sealed class ModelStatus
{
    public string Name { get; init; } = "";
    public bool Loaded { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
}
