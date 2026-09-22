using System;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ZivAiEditor.UI.Editing;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Editor toolbar (Step 9C.2): five left-slot buttons driving a
/// <see cref="ToolStateMachine"/>. Framework only — ClearMask / Undo raise events for
/// later steps (9C.3 / 9C.4) and do no drawing themselves. Enablement / selection are
/// pushed from state in code-behind (the toolbar sits inside a plain ContentPresenter,
/// so it has no DataContext owner). The buttons are marked with the <c>User</c>
/// decoration role so the OS treats them as client content inside the title bar
/// (otherwise the caption hit-test swallows their clicks).
/// </summary>
public partial class EditorToolbar : UserControl
{
    private ToolStateMachine? _state;
    private ToggleButton? _crop;
    private ToggleButton? _brush;
    private ToggleButton? _eraser;
    private Button? _clearMask;
    private Button? _undo;

    public EditorToolbar()
    {
        InitializeComponent();
        Init();
    }

    /// <summary>Raised when the user asks to clear the mask (wired in 9C.3).</summary>
    public event EventHandler? ClearMaskRequested;

    /// <summary>Raised when the user asks to undo (wired in 9C.3).</summary>
    public event EventHandler? UndoRequested;

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
        _brush = this.FindControl<ToggleButton>("PART_BtnBrush");
        _eraser = this.FindControl<ToggleButton>("PART_BtnEraser");
        _clearMask = this.FindControl<Button>("PART_BtnClearMask");
        _undo = this.FindControl<Button>("PART_BtnUndo");

        // Title-bar content: mark as client ("User") so clicks reach the buttons.
        foreach (var button in new Button?[] { _crop, _brush, _eraser, _clearMask, _undo })
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

        if (_brush is not null)
        {
            _brush.Click += (_, _) => Select(ToolMode.MaskBrush);
        }

        if (_eraser is not null)
        {
            _eraser.Click += (_, _) => Select(ToolMode.Eraser);
        }

        if (_clearMask is not null)
        {
            _clearMask.Click += (_, _) => ClearMaskRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_undo is not null)
        {
            _undo.Click += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty);
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

        if (_brush is not null)
        {
            _brush.IsChecked = _state.CurrentTool == ToolMode.MaskBrush;
            _brush.IsEnabled = _state.HasImage;
        }

        if (_eraser is not null)
        {
            _eraser.IsChecked = _state.CurrentTool == ToolMode.Eraser;
            _eraser.IsEnabled = _state.HasImage;
        }

        if (_clearMask is not null)
        {
            _clearMask.IsEnabled = _state.CanClearMask;
        }

        if (_undo is not null)
        {
            _undo.IsEnabled = _state.CanUndo;
        }
    }
}
