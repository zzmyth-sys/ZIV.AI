using Xunit;

namespace ZivAiEditor.Tests;

/// <summary>
/// Serializes every test class that touches the process-wide imaging temp roots
/// (<c>_cache/crops</c> / <c>_cache/masks</c> / <c>_cache/proxies</c>). Those roots live under
/// <c>AppContext.BaseDirectory</c> and are shared by the whole test assembly, so a class that
/// wipes a root globally must not run in parallel with a class that is mid-write into it.
///
/// <para><b>Who must join</b>: any test class that calls <c>MaskExporter.CleanupAll</c> /
/// <c>ImageCropper.CleanupAll</c> / <c>ProxyImageCache.CleanupAll</c>, or calls
/// <c>AppContext.Create()</c> (which implicitly runs <c>ImagingService.CleanupAll</c> and wipes
/// all three roots). Mark it with <c>[Collection(ImagingGlobalCleanupCollection.Name)]</c>.</para>
///
/// <para><b>If a class is left out</b>: it runs in parallel with this collection's members; one
/// test's global wipe can delete another's just-written session directory, producing intermittent
/// failures such as <c>MaskExporterTests.TryLoad_Of_Feathered_Png_Recovers_Hard_Contour</c>
/// returning <c>null</c> (flake first seen at commit c8c9fd3). Marking the collection
/// non-parallel keeps them isolated without changing the production cleanup semantics.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ImagingGlobalCleanupCollection
{
    public const string Name = "ImagingGlobalCleanup";
}
