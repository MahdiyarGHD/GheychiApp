using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Gheychi.App.Ui;

namespace Gheychi.App;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeState.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new MainView();

        base.OnFrameworkInitializationCompleted();
    }
}
