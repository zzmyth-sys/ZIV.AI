using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Path = Avalonia.Controls.Shapes.Path;

namespace ZivAiEditor.App.Controls;

/// <summary>
/// Shared self-drawn window chrome (Step 9C.2): drop shadow + rounded root + title bar
/// (title text and minimize / maximize / close buttons) + 8-way resize strips. The
/// hosting window supplies its content through <see cref="Body"/> (a dedicated property,
/// not <c>Content</c>, because the UserControl's <c>Content</c> is its own XAML root).
/// </summary>
public partial class ChromeTitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<ChromeTitleBar, string>(nameof(TitleText), "");

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<ChromeTitleBar, object?>(nameof(Body));

    private readonly ContentPresenter? _body;
    private readonly TextBlock? _title;

    public ChromeTitleBar()
    {
        InitializeComponent();

        TitleBar = this.FindControl<Border>("PART_TitleBar");
        BtnMinimize = this.FindControl<Button>("PART_BtnMinimize");
        BtnMaximize = this.FindControl<Button>("PART_BtnMaximize");
        BtnClose = this.FindControl<Button>("PART_BtnClose");
        IconMaximize = this.FindControl<Path>("PART_IconMaximize");
        _body = this.FindControl<ContentPresenter>("PART_Body");
        _title = this.FindControl<TextBlock>("PART_TitleText");

        if (_title is not null)
        {
            _title.Text = TitleText;
        }

        if (_body is not null)
        {
            _body.Content = Body;
        }
    }

    /// <summary>Caption text shown in the title bar.</summary>
    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
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

        if (change.Property == TitleTextProperty && _title is not null)
        {
            _title.Text = change.NewValue as string ?? "";
        }
        else if (change.Property == BodyProperty && _body is not null)
        {
            _body.Content = change.NewValue;
        }
    }
}