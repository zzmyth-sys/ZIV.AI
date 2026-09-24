using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ZivAiEditor.App.Controls;
using ZivAiEditor.UI.Chat;
using Path = Avalonia.Controls.Shapes.Path;

namespace ZivAiEditor.App;

/// <summary>
/// Chat-bubble rendering half of <see cref="MainWindow"/> (Step 9C.8-B2): the bubble action
/// row (× cancel / regenerate), its button builder and the image preview. Split out of the
/// main file to keep each file within the Z8 budget.
/// </summary>
public partial class MainWindow
{
    /// <summary>A row with the status text (fills) and the action button docked right.</summary>
    private static Control BuildActionRow(TextBlock? text, Button? action)
    {
        if (text is null)
        {
            return (Control?)action ?? new Panel();
        }

        if (action is null)
        {
            return text;
        }

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(action, Dock.Right);
        dock.Children.Add(action);
        dock.Children.Add(text);
        return dock;
    }

    /// <summary>Builds the × (pending) / regenerate (rerunnable) bubble action button.</summary>
    private Button? BuildBubbleAction(ChatMessage message, string? rerunNodeId)
    {
        if (message.IsPending)
        {
            // Step 9C.8-B: cancel the in-flight run (title-bar close style, 75%-transparent
            // rounded base).
            var cancel = new Button
            {
                Classes = { "bubbleAction", "bubbleClose" },
                Content = new Path { Classes = { "bubbleIcon" }, Data = BubbleIcon("IconClose") },
            };
            ToolTip.SetTip(cancel, "取消");
            cancel.Click += (_, _) =>
            {
                cancel.IsEnabled = false;
                SetStatus("取消中…");
                _vm.CancelCurrent();
            };
            return cancel;
        }

        if (rerunNodeId is { Length: > 0 })
        {
            // Once the image is generated the × becomes a regenerate (re-run) button,
            // mirroring the history-node menu.
            var regenerate = new Button
            {
                Classes = { "bubbleAction" },
                Content = new Path { Classes = { "bubbleIcon" }, Data = BubbleIcon("IconRefresh") },
            };
            ToolTip.SetTip(regenerate, "重新生成");
            regenerate.Click += (_, _) => _ = RerunAsync(rerunNodeId);
            return regenerate;
        }

        return null;
    }

    private static Border BuildBubbleBorder(ChatMessage message, Control child) => new()
    {
        Background = message.Role == ChatRole.User ? UserBubbleBrush : AssistantBubbleBrush,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(7, 6),
        HorizontalAlignment = message.Role == ChatRole.User
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left,
        Child = child,
    };

    private void AddPreview(Panel panel, string path, string? maskPath = null, int maskFeatherPx = 0)
    {
        try
        {
            var bitmap = new Bitmap(path);
            _bitmaps.Add(bitmap);

            // Clicking a chat image opens the standalone large-image preview window.
            var image = new Image
            {
                Source = bitmap,
                MaxWidth = BubbleImageSize,
                MaxHeight = BubbleImageSize,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            image.PointerPressed += (_, e) =>
            {
                // Only the left button opens the large preview; the right button is
                // reserved for the node's context menu (Step 9C.8-A: "重跑").
                if (!e.GetCurrentPoint(image).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                e.Handled = true;
                OpenImagePreview(path);
            };
            AttachRerunMenuToImage(image, path);
            ToolTip.SetTip(image, "左键查看大图 / 右键重跑");

            AddImageWithMaskOverlay(panel, image, bitmap, maskPath, maskFeatherPx, overlay =>
            {
                overlay.MaxWidth = BubbleImageSize;
                overlay.MaxHeight = BubbleImageSize;
                overlay.HorizontalAlignment = HorizontalAlignment.Left;
            });
        }
        catch (Exception ex)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"[预览失败] {ex.Message}",
                Foreground = ErrorBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    /// <summary>
    /// Adds <paramref name="image"/> to <paramref name="panel"/>, wrapping it in a Grid with a
    /// display-only mask overlay when the node has a mask (E1). The overlay is loaded off the
    /// UI thread and applied only while its chat generation is still current.
    /// </summary>
    private void AddImageWithMaskOverlay(
        Panel panel,
        Image image,
        Bitmap original,
        string? maskPath,
        int maskFeatherPx,
        Action<Image> configureOverlay)
    {
        if (string.IsNullOrWhiteSpace(maskPath) || !File.Exists(maskPath))
        {
            if (!string.IsNullOrWhiteSpace(maskPath))
            {
                MaskDiagnostics.Log($"[overlay] skip (file missing): {maskPath}");
            }

            panel.Children.Add(image);
            return;
        }

        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        host.Children.Add(image);
        panel.Children.Add(host);

        var generation = _chatGeneration;
        _ = AddMaskOverlayAsync(host, original.PixelSize, maskPath, maskFeatherPx, generation, configureOverlay);
    }

    /// <summary>
    /// Decodes the mask PNG, applies the same feather the preview / export use, and adds a
    /// semi-transparent red overlay image aligned with the bubble image (E1, UI only).
    /// </summary>
    private async Task AddMaskOverlayAsync(
        Grid host,
        PixelSize size,
        string maskPath,
        int maskFeatherPx,
        int generation,
        Action<Image> configureOverlay)
    {
        var loaded = await _imaging.LoadMaskAsync(maskPath);
        if (generation != _chatGeneration || loaded is not { } data)
        {
            MaskDiagnostics.Log(
                $"[overlay] skip (gen {generation}/{_chatGeneration}, load={(loaded is null ? "null" : "ok")}): {maskPath}");
            return;
        }

        // The mask lives in the pipeline-image pixel space, so it must match the bubble image.
        if (data.Width != size.Width || data.Height != size.Height)
        {
            MaskDiagnostics.Log(
                $"[overlay] skip (size {data.Width}x{data.Height} vs {size.Width}x{size.Height}): {maskPath}");
            return;
        }

        var display = maskFeatherPx > 0
            ? _imaging.FeatherMask(data.Pixels, data.Width, data.Height, maskFeatherPx)
            : data.Pixels;

        var overlay = MaskOverlayBitmap.Build(display, data.Width, data.Height);
        if (overlay is null || generation != _chatGeneration)
        {
            MaskDiagnostics.Log($"[overlay] skip (build null or gen {generation}/{_chatGeneration}): {maskPath}");
            overlay?.Dispose();
            return;
        }

        _bitmaps.Add(overlay);
        var image = new Image
        {
            Source = overlay,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
        configureOverlay(image);
        host.Children.Add(image);
        MaskDiagnostics.Log($"[overlay] applied {data.Width}x{data.Height} feather={maskFeatherPx}: {maskPath}");
    }

    /// <summary>
    /// Renders a message's image(s) (Step 9C.10): a multi-image bubble (an image-pack root)
    /// shows a horizontal thumbnail row, otherwise the existing single preview is used. E1: a
    /// node's mask overlays its <b>main</b> image (the first thumbnail for a pack).
    /// </summary>
    private void AddMessageImages(Panel panel, ChatMessage message)
    {
        if (message.ImagePaths.Count > 1)
        {
            AddPreviewPack(panel, message.ImagePaths, message.MaskPath, message.MaskFeatherPx);
        }
        else if (message.ImagePath is { Length: > 0 } path)
        {
            AddPreview(panel, path, message.MaskPath, message.MaskFeatherPx);
        }
    }

    /// <summary>Chat image-pack thumbnail size (Step 9C.10, Q4).</summary>
    private const double PackThumbSize = 72;

    /// <summary>
    /// Renders the image-pack bubble (Step 9C.10, Q4): up to four 72px thumbnails in a
    /// horizontal row plus a "+N" label when the pack is larger. Each thumbnail reuses the
    /// tracked bitmap path (Z9) and the click-to-preview / rerun-menu behavior.
    /// </summary>
    private void AddPreviewPack(Panel panel, IReadOnlyList<string> paths, string? maskPath = null, int maskFeatherPx = 0)
    {
        const int maxThumbs = 4;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var shown = Math.Min(paths.Count, maxThumbs);
        for (var i = 0; i < shown; i++)
        {
            // E1: the mask belongs to the node's main image, i.e. the first thumbnail.
            AddPackThumb(row, paths[i], i == 0 ? maskPath : null, maskFeatherPx);
        }

        if (paths.Count > shown)
        {
            row.Children.Add(new TextBlock
            {
                Text = $"+{paths.Count - shown}",
                Foreground = SecondaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            });
        }

        panel.Children.Add(row);
    }

    private void AddPackThumb(Panel row, string path, string? maskPath = null, int maskFeatherPx = 0)
    {
        try
        {
            var bitmap = new Bitmap(path);
            _bitmaps.Add(bitmap);

            var image = new Image
            {
                Source = bitmap,
                Width = PackThumbSize,
                Height = PackThumbSize,
                Stretch = Stretch.Uniform,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            image.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(image).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                e.Handled = true;
                OpenImagePreview(path);
            };
            AttachRerunMenuToImage(image, path);
            ToolTip.SetTip(image, "左键查看大图 / 右键重跑");

            AddImageWithMaskOverlay(row, image, bitmap, maskPath, maskFeatherPx, overlay =>
            {
                overlay.Width = PackThumbSize;
                overlay.Height = PackThumbSize;
            });
        }
        catch (Exception ex)
        {
            row.Children.Add(new TextBlock
            {
                Text = $"[预览失败] {ex.Message}",
                Foreground = ErrorBrush,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    /// <summary>Resolves a Tabler icon geometry for a chat-bubble action button.</summary>
    private Geometry BubbleIcon(string key)
        => this.TryFindResource(key, out var resource) && resource is Geometry geometry
            ? geometry
            : Geometry.Parse("M18 6l-12 12 M6 6l12 12");
}
