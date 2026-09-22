using System.Collections.Concurrent;
using ZivAiEditor.Contracts.Tools;

namespace ZivAiEditor.Tools;

/// <summary>
/// Thread-safe <see cref="IToolRegistry"/> backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Registration is by
/// <see cref="IEditTool.Name"/>; re-registering the same name replaces the
/// previous tool. No extra abstraction (ARCHITECTURE.md §11).
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, IEditTool> _tools = new(StringComparer.Ordinal);

    public void Register(IEditTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            throw new ArgumentException("IEditTool.Name is required.", nameof(tool));
        }

        _tools[tool.Name] = tool;
    }

    public bool Unregister(string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        return _tools.TryRemove(toolName, out _);
    }

    public IEditTool? Get(string toolName)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            return null;
        }

        return _tools.TryGetValue(toolName, out var tool) ? tool : null;
    }

    public IReadOnlyList<IEditTool> All => _tools.Values.ToArray();
}
