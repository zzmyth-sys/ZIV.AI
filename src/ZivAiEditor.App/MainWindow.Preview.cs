using System;
using Avalonia.Controls;
using ZivAiEditor.App.Controls;
using ZivAiEditor.Contracts.Planning;

namespace ZivAiEditor.App;

/// <summary>
/// Preview-window half of <see cref="MainWindow"/> (Step 9C.1 / 9C.6-B): opens the
/// standalone large-image preview and resolves which session node it shows. Split out of
/// the main file to keep each file under the Z8 line budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Opens (or reuses) the standalone large-image preview window for
    /// <paramref name="path"/> (Step 9C.1). A single window instance is kept: a second
    /// click loads the new image into the same window, preserving its position / size.
    /// Step 9C.6-B: the node owning <paramref name="path"/> is resolved so crop edits target
    /// it and the preview shows its crop result.
    /// </summary>
    private void OpenImagePreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            SetStatus("图像不存在，无法预览");
            return;
        }

        var node = FindNodeByImagePath(path);

        if (_imagePreview is null)
        {
            var preview = new ImagePreview();
            preview.Closed += (_, _) =>
            {
                if (ReferenceEquals(_imagePreview, preview))
                {
                    _imagePreview = null;
                }
            };

            // Step 9C.6-B: a confirmed crop updates the node's intrinsic crop.
            preview.CropCompleted += OnPreviewCropCompleted;

            _imagePreview = preview;
            preview.Show(this);
        }
        else if (_imagePreview.WindowState == WindowState.Minimized)
        {
            _imagePreview.WindowState = WindowState.Normal;
        }

        // The clicked path may be the node's own image or its crop result; always pass the
        // node's own image as the crop source so re-cropping never chains.
        _imagePreview.LoadNode(node?.NodeId, node?.ImagePath ?? path, node?.Crop);

        // Swipe-compare reference: the parent node's pipeline image (Step 9C.6-B); null
        // for the root image, which disables the compare button.
        _imagePreview.SetCompareSource(_vm.GetParentPipelineImagePath(path));
        _imagePreview.Activate();
    }

    /// <summary>
    /// The session node whose own image <b>or crop result</b> is <paramref name="path"/>
    /// (chat bubbles show the crop result, so a click can carry either), if any.
    /// </summary>
    private IEditNode? FindNodeByImagePath(string path)
    {
        foreach (var node in _vm.Session.GetHistory())
        {
            if (string.Equals(node.ImagePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (node.Crop is { ResultImagePath.Length: > 0 } crop
                && string.Equals(crop.ResultImagePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }

        return null;
    }
}
