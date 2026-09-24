using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ZivAiEditor.App.Controls;
using ZivAiEditor.Contracts.Session;

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
            var preview = new ImagePreview(_imaging);
            _shell.ApplyChrome(preview);
            preview.Closed += (_, _) =>
            {
                if (ReferenceEquals(_imagePreview, preview))
                {
                    _imagePreview = null;
                }
            };

            // Step 9C.6-B: a confirmed crop updates the node's intrinsic crop.
            preview.CropCompleted += OnPreviewCropCompleted;

            // Step 9C.7: a completed mask draw updates the node's intrinsic mask.
            preview.MaskCompleted += OnPreviewMaskCompleted;

            // Step 9C.6-E: "save as" is handled here (the App owns the picker / session).
            preview.SaveRequested += (_, _) => _ = SavePreviewImageAsync(preview);

            _imagePreview = preview;
            preview.Show(this);
        }
        else if (_imagePreview.WindowState == WindowState.Minimized)
        {
            _imagePreview.WindowState = WindowState.Normal;
        }

        // The clicked path may be the node's own image or its crop result; always pass the
        // node's own image as the crop source so re-cropping never chains.
        _imagePreview.LoadNode(_vm.Session.SessionId, node?.NodeId, node?.ImagePath ?? path, node?.Crop, node?.Mask);

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

    /// <summary>
    /// "Save as" for the preview window (Step 9C.6-E): the default directory is the source
    /// image's directory and the default name is
    /// <c>{first8 of source}_{command name or first8 of prompt}.png</c>. The copy runs off
    /// the UI thread (Z11).
    /// </summary>
    private async Task SavePreviewImageAsync(ImagePreview preview)
    {
        // Save what is actually shown (the crop result when one exists), not the raw node image.
        var path = preview.DisplayPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetStatus("没有可保存的图像");
            return;
        }

        // Shell-domain facade (module-boundary migration step 5): the picker lives in ShellService.
        var node = FindNodeByImagePath(path);
        var rootPath = _session.RootImagePath;
        var startDirectory = string.IsNullOrWhiteSpace(rootPath) ? null : Path.GetDirectoryName(rootPath);

        var target = await _shell.PickSaveFileAsync(this, BuildSaveName(rootPath, node?.Command), startDirectory);
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            await Task.Run(() => File.Copy(path, target, overwrite: true));
            SetStatus($"已另存为：{Path.GetFileName(target)}");
        }
        catch (Exception ex)
        {
            SetStatus($"另存失败：{ex.Message}");
        }
    }

    private static string BuildSaveName(string? rootPath, string? command)
    {
        var source = string.IsNullOrWhiteSpace(rootPath) ? "" : Path.GetFileNameWithoutExtension(rootPath);
        var first = Take8(source);
        var second = Take8(CommandLabel(command));
        var name = string.IsNullOrEmpty(second) ? first : $"{first}_{second}";
        name = SanitizeFileName(name);
        return string.IsNullOrEmpty(name) ? "image.png" : name + ".png";
    }

    private static string CommandLabel(string? command)
    {
        var text = (command ?? "").Trim();
        if (text.Length == 0)
        {
            return "";
        }

        if (text[0] == '/')
        {
            var space = text.IndexOf(' ');
            var token = space < 0 ? text : text[..space];
            return token.TrimStart('/');
        }

        return text;
    }

    private static string Take8(string value) => value.Length <= 8 ? value : value[..8];

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        return builder.ToString().Trim();
    }
}
