using System;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Editor mode toolbar (N4): two left-slot toggles driving a <see cref="ToolStateMachine"/>
/// — crop and mask (mask selects <see cref="ToolMode.MaskBrush"/>). The mask sub-actions
/// (eraser / clear / undo) live in <c>MaskModePanel</c>. Enablement / selection are pushed
/// from state in code-behind (the toolbar sits inside a plain ContentPresenter, so it has no
/// DataContext owner). The buttons are marked with the <c>User</c> decoration role so the OS
/// treats them as client content inside the title bar (otherwise the caption hit-test
/// swallows their clicks).
/// </summary>
public partial class EditorToolbar : UserControl
{
    private ToolStateMachine? _state;
    private ToggleButton? _crop;
    private ToggleButton? _mask;

    public EditorToolbar()
    {
        InitializeComponent();
        Init();
    }

    /// <summary>Binds the toolbar to a state machine and refreshes from it.</summary>
    public void Attach(ToolStateMachine state)
    {
        if (_state is not null)
        {
            _state.StateChanged -= OnStateChanged;
        }

        _state = state ?? throw new ArgumentNullException(nameof(state));
        _state.StateChanged += OnStateChanged;
        Refresh();
    }

    private void Init()
    {
        _crop = this.FindControl<ToggleButton>("PART_BtnCrop");
        _mask = this.FindControl<ToggleButton>("PART_BtnMask");

        // Title-bar content: mark as client ("User") so clicks reach the buttons.
        foreach (var button in new ToggleButton?[] { _crop, _mask })
        {
            if (button is not null)
            {
                WindowDecorationProperties.SetElementRole(button, WindowDecorationsElementRole.User);
            }
        }

        if (_crop is not null)
        {
            _crop.Click += (_, _) => Select(ToolMode.Crop);
        }

        if (_mask is not null)
        {
            _mask.Click += (_, _) => Select(ToolMode.MaskBrush);
        }
    }

    private void Select(ToolMode mode)
    {
        _state?.SetTool(mode);

        // Force the toggles back in sync even when the tool did not change: a click on
        // the already-active toggle flips IsChecked before Click fires.
        Refresh();
    }

    private void OnStateChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (_state is null)
        {
            return;
        }

        if (_crop is not null)
        {
            _crop.IsChecked = _state.CurrentTool == ToolMode.Crop;
            _crop.IsEnabled = _state.CanCrop;
        }

        if (_mask is not null)
        {
            _mask.IsChecked = _state.CurrentTool is ToolMode.MaskBrush or ToolMode.Eraser;
            _mask.IsEnabled = _state.HasImage;
        }
    }
}
