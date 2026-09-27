using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(ZivAiEditor.Tests.UI.TestApplication))]

namespace ZivAiEditor.Tests.UI;

/// <summary>
/// Headless test host application (Z-008 groundwork). Mirrors the product
/// <c>App.axaml</c> resource/style graph so the App's window XAML can load in tests
/// without running the product composition root. A test-local application is used
/// instead of <c>ZivAiEditor.App.App</c> because the latter's
/// <c>OnFrameworkInitializationCompleted</c> builds <c>AppContext.Create(Shell!)</c>
/// with a null shell on the desktop lifetime path.
///
/// <para>Mirrors <c>src/ZivAiEditor.App/App.axaml</c>: FluentTheme + ChromeStyles +
/// ZivColors + TablerIcons. <c>MainWindow.axaml</c> resolves those via
/// <c>{StaticResource ...}</c> at load time, so they must exist before construction.</para>
/// </summary>
public class TestApplication : Application
{
    public override void Initialize()
    {
        var appBase = new Uri("avares://ZivAiEditor.App/");

        // Resources must be populated before the styles that reference them
        // (ChromeStyles.axaml uses {StaticResource ZivButtonHoverBrush} ...), matching
        // the declaration order in App.axaml.
        Resources.MergedDictionaries.Add(new ResourceInclude(appBase)
        {
            Source = new Uri("avares://ZivAiEditor.App/Themes/ZivColors.axaml"),
        });
        Resources.MergedDictionaries.Add(new ResourceInclude(appBase)
        {
            Source = new Uri("avares://ZivAiEditor.App/Assets/Icons/TablerIcons.axaml"),
        });

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(appBase)
        {
            Source = new Uri("avares://ZivAiEditor.App/Styles/ChromeStyles.axaml"),
        });
    }
}
