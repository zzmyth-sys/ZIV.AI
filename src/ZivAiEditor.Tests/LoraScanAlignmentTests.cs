using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using ZivAiEditor.App;

namespace ZivAiEditor.Tests;

/// <summary>
/// Cross-language alignment guard: the C# <see cref="SettingsWindow.ScanPublicLoras(string?, string?)"/>
/// and the Python <c>lora-manager.scan_public</c> must agree on the **same shared fixture**
/// (<c>tests/fixtures/lora-scan</c>). Both read the same <c>expected.json</c>; a rule change on
/// either side fails this test (and its Python sibling). Only the set is asserted — the sort
/// differs by design (C# OrdinalIgnoreCase vs Python sorted).
/// </summary>
public class LoraScanAlignmentTests
{
    [Fact]
    public void CSharp_ScanPublicLoras_Matches_Shared_Expected()
    {
        var fixture = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "lora-scan");
        var loraRoot = Path.Combine(fixture, "models", "loras");
        var lorasJson = Path.Combine(fixture, "loras.json");

        var expected = JsonSerializer
            .Deserialize<ExpectedDto>(File.ReadAllText(Path.Combine(fixture, "expected.json")))!
            .PublicLoras;

        var actual = SettingsWindow.ScanPublicLoras(loraRoot, lorasJson);

        var expectedSet = new HashSet<string>(expected, StringComparer.OrdinalIgnoreCase);
        var actualSet = new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase);
        Assert.True(
            expectedSet.SetEquals(actualSet),
            $"expected=[{string.Join(",", expected)}] actual=[{string.Join(",", actual)}]");
        // The .ckpt sample in the fixture guards the extension-set rule (D1).
        Assert.Contains("d.ckpt", actualSet);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DOC", "FROZEN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("repository root (DOC/FROZEN.md) not found");
    }

    private sealed class ExpectedDto
    {
        [JsonPropertyName("public_loras")]
        public List<string> PublicLoras { get; init; } = new();
    }
}
