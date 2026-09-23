using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Imaging;
using ZivAiEditor.UI.Editing;
using Path = Avalonia.Controls.Shapes.Path;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Attachment strip (Step 9C.3, web-chat style): a horizontal row of dashed thumbnails
/// that is <b>hidden by default</b> and appears above the input box once images are
/// dragged in or picked. Hovering a thumbnail reveals an <c>×</c> to remove it. The path
/// list lives in the pure, unit-tested <see cref="ImageImportList"/>; this control
/// renders it and decodes thumbnails off the UI thread (Z11), disposing them when pruned
/// or when the window closes (Z9).
///
/// <para>The control is <b>platform-light</b>: the file picker and OS drag-drop are owned
/// by <c>MainWindow</c>, which feeds paths through <see cref="AddFiles"/>.</para>
/// </summary>
public partial class ImageImportBar : UserControl
{
    private readonly ImageImportList _list = new();
    private readonly Dictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);

    private ScrollViewer? _scroll;
    private StackPanel? _items;
    private bool _disposed;

    public ImageImportBar()
    {
        InitializeComponent();
        Init();
    }

    /// <summary>Forwarded from <see cref="ImageImportList.ImagesChanged"/> (carries counts).</summary>
    public event EventHandler<ImageImportChangedEventArgs>? ImagesChanged;

    /// <summary>The imported image paths, oldest first (diagnostics / tests).</summary>
    public IReadOnlyList<string> Paths => _list.Paths;

    /// <summary>Number of imported images (diagnostics / tests).</summary>
    public int Count => _list.Count;

    /// <summary>True when at least one image is attached.</summary>
    public bool HasImages => _list.HasImages;

    /// <summary>Adds imported paths (blanks and duplicates are skipped; one batch = one change).</summary>
    public void AddFiles(IEnumerable<string>? paths) => _list.AddRange(paths);

    /// <summary>Removes the thumbnail at <paramref name="index"/>.</summary>
    public void RemoveAt(int index) => _list.RemoveAt(index);

    /// <summary>Removes every attachment.</summary>
    public void Clear() => _list.Clear();

    private void Init()
    {
        _scroll = this.FindControl<ScrollViewer>("PART_Scroll");
        _items = this.FindControl<StackPanel>("PART_Items");

        _list.ImagesChanged += (_, e) =>
        {
            Rebuild();
            ImagesChanged?.Invoke(this, e);
        };

        Rebuild();
    }

    /// <summary>Rebuilds the strip and shows it only when there is at least one image.</summary>
    private void Rebuild()
    {
        if (_items is null)
        {
            return;
        }

        PruneCache();

        _items.Children.Clear();
        for (var i = 0; i < _list.Count; i++)
        {
            _items.Children.Add(BuildThumb(_list.Paths[i], i));
        }

        if (_scroll is not null)
        {
            _scroll.IsVisible = _list.Count > 0;
        }
    }

    private Control BuildThumb(string path, int index)
    {
        var panel = new Panel { Width = 72, Height = 72, Classes = { "thumb" } };
        panel.Children.Add(new Rectangle { Classes = { "thumbFrame" } });

        var image = new Image
        {
            Width = 62,
            Height = 62,
            Stretch = Avalonia.Media.Stretch.Uniform,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        if (_cache.TryGetValue(path, out var cached))
        {
            image.Source = cached;
        }
        else
        {
            _ = LoadThumbAsync(path, image);
        }

        panel.Children.Add(image);

        var remove = new Button
        {
            Classes = { "remove" },
            Content = new Path { Classes = { "removeIcon" }, Data = CloseGeometry() },
        };
        ToolTip.SetTip(remove, "移除");
        remove.Click += (_, _) => _list.RemoveAt(index);
        panel.Children.Add(remove);

        return panel;
    }

    private async Task LoadThumbAsync(string path, Image target)
    {
        Bitmap? bitmap = null;
        var failed = false;

        await Task.Run(() =>
        {
            try
            {
                bitmap = new Bitmap(path);
            }
            catch (Exception ex)
            {
                failed = true;
                System.Diagnostics.Debug.WriteLine($"[import] {ex.Message}");
            }
        });

        if (_disposed || failed || bitmap is null)
        {
            bitmap?.Dispose();
            return;
        }

        if (_cache.ContainsKey(path))
        {
            // A concurrent load won the race; keep the first, drop this one.
            bitmap.Dispose();
            return;
        }

        _cache[path] = bitmap;
        target.Source = bitmap;
    }

    /// <summary>Disposes cached thumbnails no longer in the list.</summary>
    private void PruneCache()
    {
        if (_cache.Count == 0)
        {
            return;
        }

        var keep = new HashSet<string>(_list.Paths, StringComparer.OrdinalIgnoreCase);
        foreach (var key in _cache.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _cache[key].Dispose();
            _cache.Remove(key);
        }
    }

    /// <summary>Disposes every decoded thumbnail (Z9); called when the window closes.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var bitmap in _cache.Values)
        {
            bitmap.Dispose();
        }

        _cache.Clear();
    }

    private Avalonia.Media.Geometry CloseGeometry()
    {
        if (this.TryFindResource("IconClose", out var res) && res is Avalonia.Media.Geometry geometry)
        {
            return geometry;
        }

        return Avalonia.Media.Geometry.Parse("M18 6l-12 12 M6 6l12 12");
    }
}