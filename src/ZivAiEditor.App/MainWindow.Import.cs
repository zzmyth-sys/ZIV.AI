using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ZivAiEditor.App.Controls;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App;

/// <summary>
/// Image-import half of <see cref="MainWindow"/> (Step 9C.3): the file picker, the
/// window-level OS drag-drop, and the "single import becomes the main image" rule.
/// Split out of the main file to keep each file under the Z8 line budget.
/// </summary>
public partial class MainWindow
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff",
    };

    private ImageImportBar? _importBar;

    /// <summary>
    /// Wires the attachment strip (web-chat style): the input-row image button opens the
    /// file picker (owned here, mirroring <see cref="PickFolderAsync"/>), OS drag-drop is
    /// handled at the window level (robust under the self-drawn chrome), and a single
    /// imported image becomes the session main image.
    /// </summary>
    private void InitImport()
    {
        _importBar = this.FindControl<ImageImportBar>("PART_ImportBar");
        if (_importBar is not null)
        {
            _importBar.ImagesChanged += OnImagesChanged;
        }

        if (this.FindControl<Button>("PART_BtnAddImage") is { } addImage)
        {
            addImage.Click += async (_, _) => await AddImagesAsync();
        }

        if (this.FindControl<Button>("PART_BtnMode") is { } modeButton)
        {
            modeButton.Click += (_, _) => ToggleMode();
        }

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async Task AddImagesAsync()
    {
        var paths = await PickImagesAsync();
        if (paths.Count > 0)
        {
            _importBar?.AddFiles(paths);
        }
    }

    private void OnImagesChanged(object? sender, ImageImportChangedEventArgs e)
    {
        // Step 9C.6-C: the strip is a one-shot input. A drag/paste only fills the strip;
        // it must NOT become the session root here. Reaching two attachments auto-selects
        // multi-image mode; the user may override it manually.
        if (_importBar is { Count: >= 2 } && _vm.Mode == ImageEditMode.Single)
        {
            _vm.Mode = ImageEditMode.Multi;
            UpdateModeButton();
        }

        MaybeShowModeMismatchHint();
        UpdateSendEnabled();
    }

    private async Task<IReadOnlyList<string>> PickImagesAsync()
    {
        var storage = StorageProvider;
        if (storage is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择图片",
                AllowMultiple = true,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            });

            return files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Cast<string>()
                .ToArray();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            return Array.Empty<string>();
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasImageFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = GetImagePaths(e).ToArray();
        if (paths.Length > 0)
        {
            _importBar?.AddFiles(paths);
        }

        e.Handled = true;
    }

    private static bool HasImageFiles(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?.Any(file => IsImagePath(file.TryGetLocalPath())) == true;

    private static IEnumerable<string> GetImagePaths(DragEventArgs e)
        => e.DataTransfer.TryGetFiles()?
            .Select(file => file.TryGetLocalPath())
            .Where(IsImagePath)
            .Cast<string>()
           ?? Enumerable.Empty<string>();

    private static bool IsImagePath(string? path)
        => !string.IsNullOrEmpty(path)
           && ImageExtensions.Contains(System.IO.Path.GetExtension(path));
}