using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Serializes the tests that share the process-wide runtime proxy cache (<c>_cache/proxies</c>).
/// Each of them calls <c>ProxyImageCache.CleanupAll</c>, which wipes that one shared directory; run
/// in parallel they would race (one test's cleanup deleting another's just-written proxy). Marking
/// the collection non-parallel keeps them isolated without changing the production cache.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DisplayProxyCollection
{
    public const string Name = "DisplayProxy";
}
