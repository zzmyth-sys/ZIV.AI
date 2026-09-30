using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
            // Shared display loader (8K proxy path); the chat/import thumbs use the same instance.
            var preview = new ImagePreview(_imaging, _displayLoader);

            // F5.2: the preview is a child window of the main window — no separate taskbar
            // entry. F5.3 (Owner / follow-on-close) is already provided by Show(this) below;
            // Window.Owner has a protected setter and cannot be assigned directly here.
            preview.ShowInTaskbar = false;

            _shell.ApplyChrome(preview);
            preview.Closed += async (_, _) =>
            {
                // F5.1: restore the main window on every close path (user X, project switch,
                // node delete, owner close). Unconditional: some paths null the field first.
                IsEnabled = true;
                SetModalDim(false);

                MaskDiagnostics.Log(
                    $"[close] preview closed edited={_maskEditedInPreview} busy={_vm.IsBusy} closing={_closing}");

                // S2: wait for any in-flight mask export before dropping the reference, so a
                // save / close right after a stroke still sees the PNG on disk.
                await FlushPendingMaskAsync();

                // R3.1: the bubble overlay is refreshed exactly once, here, and only when this
                // preview session edited a mask. No edit -> no refresh (no flicker); app
                // shutdown skips the pointless rebuild.
                if (_maskEditedInPreview)
                {
                    _maskEditedInPreview = false;
                    if (!_vm.IsBusy && !_closing && !_switchingProject)
                    {
                        MaskDiagnostics.Log("[rebuild] current=" + _vm.Session.CurrentNodeId
                            + " path=" + string.Join(",", _vm.Session.GetPathToCurrent()
                                .Select(n => n.NodeId[..6] + ":" + (n.Mask is null ? "-" : "M"))));
                        _vm.RebuildContext();
                    }
                }

                if (ReferenceEquals(_imagePreview, preview))
                {
                    _imagePreview = null;
                }
            };

            // Step 9C.6-B: a confirmed crop updates the node's intrinsic crop.
            preview.CropCompleted += OnPreviewCropCompleted;

            // Step 9C.7: a completed mask draw updates the node's intrinsic mask.
            preview.MaskCompleted += OnPreviewMaskCompleted;

            // E2: entering a mask tool aligns the working node; every export is tracked at
            // window level so it can be flushed after the preview closes.
            preview.MaskToolEntered += OnPreviewMaskToolEntered;
            preview.MaskExportScheduled += OnPreviewMaskExportScheduled;

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

        // F5.1: while the preview is open it is the only interactive window (main window,
        // including its self-drawn title bar, is disabled). Idempotent across the reuse path.
        IsEnabled = false;

        // F6: dim the main window so the preview reads as a distinct layer. Visual only.
        SetModalDim(true);
    }

    /// <summary>
    /// F6: shows / hides the full-client-area dim overlay (<c>PART_ModalDim</c>) on the main
    /// window. Visual only (<c>IsHitTestVisible=false</c>); the real disable is F5.1.
    /// </summary>
    private void SetModalDim(bool visible)
    {
        if (this.FindControl<Border>("PART_ModalDim") is { } dim)
        {
            dim.IsVisible = visible;
        }
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

        // F5/E4: the picker is owned by the active preview (the main window is disabled
        // while the preview is open), so it is not anchored to a disabled owner.
        var target = await _shell.PickSaveFileAsync(preview, BuildSaveName(rootPath, node?.Command), startDirectory);
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

    // ---- B2: off-thread preview decode + latest-frame coalescing ----

    /// <summary>The newest preview frame awaiting decode, or <c>null</c>. A later frame overwrites it.</summary>
    private byte[]? _pendingPreviewBytes;

    /// <summary>True while the single decode loop is draining frames (UI-thread state).</summary>
    private bool _decoding;

    /// <summary>Managed id of the thread that last decoded a preview frame (test-only observation; B2).</summary>
    internal int LastPreviewDecodeThreadId { get; private set; }

    /// <summary>How many decoded preview frames have been applied to the pending Image (test-only; B2).</summary>
    internal int PreviewApplyCount { get; private set; }

    /// <summary>Compressed byte length of the last applied frame (test-only observation; B2).</summary>
    internal int LastPreviewAppliedLength { get; private set; }

    /// <summary>Whether the pending bubble currently shows a preview bitmap (test-only observation; B2).</summary>
    internal bool HasPreviewImage => _pendingPreviewImage?.Source is not null;

    /// <summary>
    /// Renders a live preview JPEG (a backend <c>0x02</c> frame) into the pending bubble. Called on
    /// the UI thread by the App wiring; ignored when no bubble is pending or the bytes are empty.
    /// B2: the JPEG is decoded <b>off</b> the UI thread (Z11, mirroring <c>DisplayImageLoader</c> /
    /// <c>ImagePreview</c> / <c>ImageImportBar</c>) and only the newest frame is applied, so a fast
    /// producer cannot pile up decodes on the UI thread. Does not rebuild the chat stream, so
    /// frequent frames only update one Image (no flicker).
    /// </summary>
    public void ShowPreview(byte[] jpegBytes)
    {
        if (_pendingPreviewImage is null || jpegBytes is null || jpegBytes.Length == 0)
        {
            return;
        }

        // B2 (latest-frame-wins): keep only the newest frame. If the decode loop is already
        // draining, it will pick this frame up; intermediate frames are skipped, never queued.
        _pendingPreviewBytes = jpegBytes;
        if (_decoding)
        {
            return;
        }

        _decoding = true;
        _ = DecodeLoopAsync();
    }

    /// <summary>
    /// B2: one loop serves every queued frame — decode off the UI thread, then apply on it. Only
    /// the frame in <see cref="_pendingPreviewBytes"/> when each iteration starts is decoded, so a
    /// burst collapses to the newest frame. The loop state is UI-thread-only (every await resumes
    /// on the UI thread), so no locking is needed.
    /// </summary>
    private async Task DecodeLoopAsync()
    {
        try
        {
            while (true)
            {
                var bytes = _pendingPreviewBytes;
                if (bytes is null)
                {
                    break;
                }

                _pendingPreviewBytes = null;

                Bitmap bitmap;
                try
                {
                    bitmap = await Task.Run(() =>
                    {
                        using var stream = new MemoryStream(bytes);
                        var decoded = new Bitmap(stream);
                        LastPreviewDecodeThreadId = Environment.CurrentManagedThreadId;
                        return decoded;
                    }).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[preview] decode failed: {ex.Message}");
                    continue;
                }

                var length = bytes.Length;
                await Dispatcher.UIThread.InvokeAsync(() => ApplyPreviewBitmap(bitmap, length));
            }
        }
        finally
        {
            _decoding = false;
        }
    }

    /// <summary>Applies a decoded preview bitmap to the pending bubble on the UI thread (B2).</summary>
    private void ApplyPreviewBitmap(Bitmap bitmap, int bytesLength)
    {
        if (_pendingPreviewImage is null)
        {
            // The pending bubble was rebuilt away while decoding: drop the frame.
            bitmap.Dispose();
            return;
        }

        _pendingPreviewBitmap?.Dispose();
        _pendingPreviewBitmap = bitmap;
        _pendingPreviewImage.Source = bitmap;
        _pendingPreviewImage.IsVisible = true;
        LastPreviewAppliedLength = bytesLength;
        PreviewApplyCount++;
    }
}
