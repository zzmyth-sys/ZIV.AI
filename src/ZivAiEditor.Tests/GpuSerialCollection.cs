using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Serializes test classes that drive the GPU backend. ComfyUI holds ~17 GB of
/// VRAM during a 512² inference (Z18 forbids concurrency), so classes that run
/// real inference must share one non-parallel collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GpuSerialCollection
{
    public const string Name = "ZIV.AI GPU serial";
}
