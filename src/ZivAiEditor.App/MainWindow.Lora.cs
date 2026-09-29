using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ZivAiEditor.Agent.Execution;
using ZivAiEditor.App.Controls;
using ZivAiEditor.Backend;
using ZivAiEditor.Contracts.Execution;
using ZivAiEditor.Contracts.Inference;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App;

/// <summary>
/// Command-level LoRA half of <see cref="MainWindow"/>: it shows the <see cref="LoraControl"/>
/// only for the command whose effective LoRA list is non-empty (today <c>/换脸</c>), fills it from
/// that command's current LoRA, and writes an edited value back through the template store
/// (<c>commands.user.json</c>). A save hot-reloads the command set through
/// <see cref="AppContext.ReloadCommands"/> (Z-030 复议), so it takes effect without an App
/// restart. Split out of the main file to stay under the Z8 budget.
/// </summary>
public partial class MainWindow
{
    private LoraControl? _loraControl;
    private string? _loadedLoraCommand;

    private void InitLora()
    {
        _loraControl = this.FindControl<LoraControl>("PART_LoraControl");
        if (_loraControl is null)
        {
            return;
        }

        _loraControl.FilePicker = PickLoraFileAsync;
        _loraControl.SaveRequested += OnLoraSaveRequested;

        if (FindInput() is { } input)
        {
            input.TextChanged += (_, _) => UpdateLoraVisibility();
        }
    }

    /// <summary>Shows the LoRA panel only for a command whose effective LoRA list is non-empty.</summary>
    private void UpdateLoraVisibility()
    {
        if (_loraControl is null)
        {
            return;
        }

        var command = FindLoraCommand(CommandText.FirstToken(FindInput()?.Text));
        if (command is null)
        {
            _loraControl.IsVisible = false;
            _loadedLoraCommand = null;
            return;
        }

        _loraControl.IsVisible = true;
        // Load only when the command changes, so typing the argument does not reset the edits.
        if (!string.Equals(_loadedLoraCommand, command.Name, StringComparison.Ordinal))
        {
            _loraControl.LoadFrom(BuildInitialState(command));
            _loadedLoraCommand = command.Name;
        }
    }

    private CommandDefinition? FindLoraCommand(string? token)
        => token is { Length: > 0 }
            ? _commands.FirstOrDefault(c =>
                string.Equals(c.Name, token, StringComparison.Ordinal) && c.EffectiveLoras.Count > 0)
            : null;

    private LoraUiState BuildInitialState(CommandDefinition command)
    {
        var lora = command.EffectiveLoras[0];
        var path = lora.Path ?? "";
        return new LoraUiState
        {
            Enabled = true,
            Strength = lora.StrengthModel ?? LoraControl.DefaultStrength,
            Path = path,
            DisplayName = _loraRegistry?.TryGet(path)?.Description,
        };
    }

    private Task<string?> PickLoraFileAsync()
        => _shell.PickFileAsync(this, "选择 LoRA 权重文件");

    private async void OnLoraSaveRequested(object? sender, LoraUiState state)
    {
        if (_commandTemplates is null || _loadedLoraCommand is null)
        {
            return;
        }

        try
        {
            var name = _loadedLoraCommand;

            if (state.Enabled && string.IsNullOrWhiteSpace(state.Path))
            {
                await MessageDialog.ShowAsync(this, "开启 LoRA 需要先选择一个权重文件。");
                return;
            }

            var current = _commandTemplates.List()
                .FirstOrDefault(entry => string.Equals(entry.Definition.Name, name, StringComparison.Ordinal))
                ?.Definition;
            if (current is null)
            {
                await MessageDialog.ShowAsync(this, "保存失败：未找到命令定义。");
                return;
            }

            _commandTemplates.Update(name, LoraCommandEdit.Build(current, state));

            // Update is void and swallows write errors, so verify by reading the user entry back.
            // Source must be User: the built-in /换脸 already carries a LoRA, so a failed write
            // would otherwise fall back to the built-in entry and pass the value check.
            var readBack = _commandTemplates.List()
                .FirstOrDefault(entry => string.Equals(entry.Definition.Name, name, StringComparison.Ordinal));

            if (readBack is not { Source: CommandSource.User }
                || !LoraCommandEdit.Matches(readBack.Definition, state))
            {
                await MessageDialog.ShowAsync(this, "保存失败：写入后回读校验不一致。");
                return;
            }

            // Hot-reload the command set (the parser swaps in place) so the new LoRA takes effect
            // at once; the window refreshes its own snapshot on CommandsReloaded (Z-030 复议).
            _appContext?.ReloadCommands();
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "保存失败：" + ex.Message);
        }
    }

    /// <summary>
    /// Hot-reload refresh (Z-030 复议): after <see cref="AppContext.ReloadCommands"/> swaps the
    /// parser's command set in place, mirror it into the UI snapshot and recompute LoRA visibility.
    /// Raised on the UI thread (the reload is driven from the Flyout-close path).
    /// </summary>
    private void OnCommandsReloaded(object? sender, EventArgs e)
    {
        if (_appContext is not { } context)
        {
            return;
        }

        RefreshCommands(context.Commands);
        UpdateLoraVisibility();
    }

    /// <summary>Replaces the command snapshot in place (reference swap; no new list copy).</summary>
    private void RefreshCommands(IReadOnlyList<CommandDefinition> commands) => _commands = commands;
}

/// <summary>
/// Pure helpers for the command-level LoRA write-back: a full-field clone (a class, so no
/// <c>with</c>; every field must be copied explicitly) and a read-back comparison.
/// </summary>
internal static class LoraCommandEdit
{
    /// <summary>Clones <paramref name="current"/> with the LoRA implied by <paramref name="state"/>.</summary>
    public static CommandDefinition Build(CommandDefinition current, LoraUiState state)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(state);

        return new CommandDefinition
        {
            Name = current.Name,
            Params = current.Params,
            Tool = current.Tool,
            Template = current.Template,
            Variants = current.Variants,
            DefaultVariant = current.DefaultVariant,
            Variadic = current.Variadic,
            AllowEmptyPrompt = current.AllowEmptyPrompt,
            T2i = current.T2i,
            Description = current.Description,
            FixedResolution = current.FixedResolution,
            Handler = current.Handler,
            Quick = current.Quick,
            ShortcutLabel = current.ShortcutLabel,
            Loras = state.Enabled
                ? new List<LoraOptions>
                {
                    new LoraOptions
                    {
                        Path = state.Path ?? "",
                        StrengthModel = state.Strength,
                        StrengthClip = state.Strength,
                    },
                }
                : null,
            Lora = null,
        };
    }

    /// <summary>True when the definition carries exactly the LoRA implied by <paramref name="state"/>.</summary>
    public static bool Matches(CommandDefinition? definition, LoraUiState state)
    {
        if (definition is null)
        {
            return false;
        }

        var loras = definition.EffectiveLoras;
        if (!state.Enabled)
        {
            return loras.Count == 0;
        }

        if (loras.Count != 1)
        {
            return false;
        }

        var lora = loras[0];
        return string.Equals(lora.Path ?? "", state.Path ?? "", StringComparison.Ordinal)
            && Math.Abs((lora.StrengthModel ?? LoraControl.DefaultStrength) - state.Strength) < 1e-6
            && Math.Abs((lora.StrengthClip ?? LoraControl.DefaultStrength) - state.Strength) < 1e-6;
    }
}
