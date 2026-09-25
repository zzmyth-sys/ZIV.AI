using System.Diagnostics;

namespace ZivAiEditor.Contracts.Inference;

/// <summary>
/// Resolves the effective LoRA list for the multi-slot contracts (T3.2). Shared so
/// <c>EditStep</c> / <c>ToolInput</c> / <c>EditRequest</c> de-duplicate identically.
/// </summary>
internal static class LoraSlots
{
    /// <summary>
    /// <paramref name="loras"/> when non-empty, de-duplicated by <see cref="LoraOptions.Path"/>
    /// (the first occurrence wins; a dropped duplicate is logged, never silent); otherwise
    /// <paramref name="lora"/> as a one-element list; otherwise empty.
    /// </summary>
    public static IReadOnlyList<LoraOptions> Resolve(IReadOnlyList<LoraOptions>? loras, LoraOptions? lora)
    {
        if (loras is { Count: > 0 })
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<LoraOptions>(loras.Count);
            foreach (var item in loras)
            {
                if (item is null)
                {
                    continue;
                }

                if (!seen.Add(item.Path ?? ""))
                {
                    Debug.WriteLine($"[lora] duplicate path dropped (first kept): {item.Path}");
                    continue;
                }

                result.Add(item);
            }

            return result;
        }

        return lora is { } single ? new[] { single } : Array.Empty<LoraOptions>();
    }
}
