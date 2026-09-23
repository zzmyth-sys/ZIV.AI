using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.UI.Chat;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App;

/// <summary>
/// Send half of <see cref="MainWindow"/> (Step 9C.6-C): one-shot attachment consumption,
/// the Single/Multi mode toggle, send-button validation and the transient mismatch hint.
/// Split out of the main file to keep each file under the Z8 budget.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _modeHintTimer;
    private bool _busy;

    private void InitSend()
    {
        if (FindInput() is { } input)
        {
            input.TextChanged += (_, _) => UpdateSendEnabled();
        }

        _modeHintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _modeHintTimer.Tick += (_, _) =>
        {
            _modeHintTimer!.Stop();
            if (this.FindControl<TextBlock>("PART_ModeHint") is { } hint)
            {
                hint.IsVisible = false;
            }
        };

        UpdateModeButton();
        UpdateSendEnabled();
    }

    private void ToggleMode()
    {
        _vm.Mode = _vm.Mode == ImageEditMode.Single ? ImageEditMode.Multi : ImageEditMode.Single;
        UpdateModeButton();
        MaybeShowModeMismatchHint();
        UpdateSendEnabled();
    }

    /// <summary>
    /// Shows the mode/count mismatch hint when the current selection is invalid
    /// (Step 9C.6-C): Single with 2+ attachments, or Multi with exactly one. Zero
    /// attachments is not gated by mode, so it never hints.
    /// </summary>
    private void MaybeShowModeMismatchHint()
    {
        var count = _importBar?.Count ?? 0;
        if (_vm.Mode == ImageEditMode.Single && count >= 2)
        {
            ShowModeHint("请选择多图编辑");
        }
        else if (_vm.Mode == ImageEditMode.Multi && count == 1)
        {
            ShowModeHint("请再添加一张图");
        }
    }

    private void UpdateModeButton()
    {
        if (this.FindControl<TextBlock>("PART_ModeLabel") is { } label)
        {
            label.Text = _vm.Mode == ImageEditMode.Multi ? "多图" : "单图";
        }
    }

    private void UpdateSendEnabled()
    {
        if (this.FindControl<Button>("PART_BtnSend") is not { } send)
        {
            return;
        }

        var count = _importBar?.Count ?? 0;
        send.IsEnabled = !_busy && _vm.CanSend(FindInput()?.Text, count);
    }

    private void ShowModeHint(string text)
    {
        if (this.FindControl<TextBlock>("PART_ModeHint") is not { } hint)
        {
            return;
        }

        hint.Text = text;
        hint.IsVisible = true;
        _modeHintTimer?.Stop();
        _modeHintTimer?.Start();
    }

    private async Task SubmitAsync()
    {
        if (_vm is null || FindInput() is not { } input)
        {
            return;
        }

        var text = input.Text ?? "";
        var count = _importBar?.Count ?? 0;
        if (!_vm.CanSend(text, count) || _vm.IsBusy)
        {
            return;
        }

        var attachments = _importBar?.Paths;
        var preparation = _vm.PrepareAttachments(text, attachments);
        if (preparation == AttachmentPreparation.NoImage)
        {
            _vm.AddHint("请先导入图片");
            ScrollToEnd();
            return;
        }

        // Step 9C.5-D: the first pipeline image is the main; the remaining attachments are
        // references. The DAG is reset only on "新会话"; "参考图" keeps the current node
        // (the main is the current node's pipeline image) and makes every attachment a
        // reference.
        IReadOnlyList<string> references = Array.Empty<string>();
        if (preparation == AttachmentPreparation.NeedsDecision)
        {
            var choice = await MultiImagePromptDialog.ShowAsync(this);
            if (choice == MultiImageChoice.Cancel)
            {
                return;
            }

            if (choice == MultiImageChoice.Reference)
            {
                // Copy: `attachments` is the live strip backing list, cleared below.
                references = attachments!.ToArray();
            }
            else
            {
                _vm.StartNewSessionFrom(attachments!);
                references = attachments!.Skip(1).ToArray();
                if (references.Count > 0)
                {
                    // Must follow StartNewSessionFrom, which rebuilds / clears Messages.
                    _vm.AddHint("图 2/3 作为参考图");
                }
            }
        }
        else if (attachments is { Count: > 1 })
        {
            // No root: PrepareAttachments already promoted the first attachment to the
            // root, so the remaining attachments are references.
            references = attachments.Skip(1).ToArray();
        }

        // Step 9C.6-D: the strip is consumed at send time — clear it now, before the
        // (possibly long) generation, so the input row resets immediately. A canceled
        // dialog or a validation block returns earlier and keeps the strip. The result
        // bubble reads its image from the appended node, never from the strip.
        _importBar?.Clear();

        input.Text = "";
        SetBusy(true);
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var progress = new Progress<TaskProgress>(OnProgress);
        try
        {
            await _vm.SubmitAsync(text, progress, _cts.Token, references);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
            ScrollToEnd();
        }
    }

    private void OnProgress(TaskProgress progress)
    {
        var fraction = progress.Fraction > 0 ? $" {progress.Fraction:P0}" : "";
        SetStatus($"{progress.Message}{fraction}");
    }
}