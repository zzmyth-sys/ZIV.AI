using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Path = Avalonia.Controls.Shapes.Path;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Shared self-drawn window chrome (Step 9C.2): drop shadow + rounded root + title bar
/// (left slot, centered title, minimize / maximize / close) + 8-way resize strips. The
/// hosting window supplies its content through <see cref="Body"/> (a dedicated property,
/// not <c>Content</c>, because the UserControl's <c>Content</c> is its own XAML root),
/// plus optional <see cref="LeftContent"/> / <see cref="CenterContent"/> slots that
/// mirror ZIV's title bar (function icons left, title centered).
/// </summary>
public partial class ChromeTitleBar : UserControl
{
    public static readonly StyledProperty<object?> LeftContentProperty =
        AvaloniaProperty.Register<ChromeTitleBar, object?>(nameof(LeftContent));

    public static readonly StyledProperty<object?> CenterContentProperty =
        AvaloniaProperty.Register<ChromeTitleBar, object?>(nameof(CenterContent));

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<ChromeTitleBar, object?>(nameof(RightContent));

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<ChromeTitleBar, object?>(nameof(Body));

    private readonly ContentPresenter? _left;
    private readonly ContentPresenter? _center;
    private readonly ContentPresenter? _right;
    private readonly ContentPresenter? _body;

    public ChromeTitleBar()
    {
        InitializeComponent();

        TitleBar = this.FindControl<Border>("PART_TitleBar");
        BtnMinimize = this.FindControl<Button>("PART_BtnMinimize");
        BtnMaximize = this.FindControl<Button>("PART_BtnMaximize");
        BtnClose = this.FindControl<Button>("PART_BtnClose");
        IconMaximize = this.FindControl<Path>("PART_IconMaximize");
        _left = this.FindControl<ContentPresenter>("PART_Left");
        _center = this.FindControl<ContentPresenter>("PART_Center");
        _right = this.FindControl<ContentPresenter>("PART_Right");
        _body = this.FindControl<ContentPresenter>("PART_Body");

        if (_left is not null)
        {
            _left.Content = LeftContent;
        }

        if (_center is not null)
        {
            _center.Content = CenterContent;
        }

        if (_right is not null)
        {
            _right.Content = RightContent;
        }

        if (_body is not null)
        {
            _body.Content = Body;
        }
    }

    /// <summary>Left-aligned title-bar content (e.g. the toolbar icons).</summary>
    public object? LeftContent
    {
        get => GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }

    /// <summary>Centered title-bar content (e.g. the file name).</summary>
    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    /// <summary>Right-aligned title-bar content, placed before the window buttons.</summary>
    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    /// <summary>The hosting window's content, rendered below the title bar.</summary>
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public Border? TitleBar { get; }

    public Button? BtnMinimize { get; }

    public Button? BtnMaximize { get; }

    public Button? BtnClose { get; }

    public Path? IconMaximize { get; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LeftContentProperty && _left is not null)
        {
            _left.Content = change.NewValue;
        }
        else if (change.Property == CenterContentProperty && _center is not null)
        {
            _center.Content = change.NewValue;
        }
        else if (change.Property == RightContentProperty && _right is not null)
        {
            _right.Content = change.NewValue;
        }
        else if (change.Property == BodyProperty && _body is not null)
        {
            _body.Content = change.NewValue;
        }
    }
}