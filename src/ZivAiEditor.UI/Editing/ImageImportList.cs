namespace ZivAiEditor.UI.Editing;

/// <summary>
/// Reports how the import list changed (Step 9C.3): the count before and after the
/// mutation. The App layer uses the transition to decide when an imported image
/// becomes the session main image — specifically, only an add that takes the list
/// from empty to exactly one (<c>CountBefore == 0 &amp;&amp; CountAfter == 1</c>)
/// promotes; a removal never does.
/// </summary>
public sealed class ImageImportChangedEventArgs : EventArgs
{
    /// <summary>Number of images before the change.</summary>
    public int CountBefore { get; init; }

    /// <summary>Number of images after the change.</summary>
    public int CountAfter { get; init; }
}

/// <summary>
/// Pure state for the image-import row (Step 9C.3): an ordered, de-duplicated list of
/// image paths. It carries <b>no</b> Avalonia dependency (Z3 / Z6), so the add / remove
/// / de-dupe logic is unit-testable without a UI thread; the App layer renders it and
/// owns the actual file decoding.
///
/// <para>De-duplication compares paths with <see cref="StringComparison.OrdinalIgnoreCase"/>
/// (Windows file paths are case-insensitive). A batch add raises <see cref="ImagesChanged"/>
/// exactly once, so a multi-image drop is a single atomic transition.</para>
/// </summary>
public sealed class ImageImportList
{
    private readonly List<string> _paths = new();

    /// <summary>Raised once per mutating operation that actually changed the list.</summary>
    public event EventHandler<ImageImportChangedEventArgs>? ImagesChanged;

    /// <summary>The imported image paths, oldest first.</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>Number of imported images.</summary>
    public int Count => _paths.Count;

    /// <summary>True when <paramref name="path"/> is already present (case-insensitive).</summary>
    public bool Contains(string path)
        => !string.IsNullOrWhiteSpace(path) && _paths.Any(p => Same(p, path));

    /// <summary>
    /// Adds a batch of paths, skipping blanks and duplicates. Raises <see cref="ImagesChanged"/>
    /// once when at least one path was added. Returns the number of paths added.
    /// </summary>
    public int AddRange(IEnumerable<string>? paths)
    {
        if (paths is null)
        {
            return 0;
        }

        var before = _paths.Count;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || _paths.Any(p => Same(p, path)))
            {
                continue;
            }

            _paths.Add(path);
        }

        var added = _paths.Count - before;
        if (added > 0)
        {
            RaiseChanged(before);
        }

        return added;
    }

    /// <summary>Removes the path at <paramref name="index"/>. Returns <c>false</c> when out of range.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _paths.Count)
        {
            return false;
        }

        var before = _paths.Count;
        _paths.RemoveAt(index);
        RaiseChanged(before);
        return true;
    }

    /// <summary>Removes every path. No-op (and no event) when already empty.</summary>
    public void Clear()
    {
        if (_paths.Count == 0)
        {
            return;
        }

        var before = _paths.Count;
        _paths.Clear();
        RaiseChanged(before);
    }

    private void RaiseChanged(int before)
        => ImagesChanged?.Invoke(this, new ImageImportChangedEventArgs
        {
            CountBefore = before,
            CountAfter = _paths.Count,
        });

    private static bool Same(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}