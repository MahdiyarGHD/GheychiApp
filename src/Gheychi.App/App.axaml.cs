using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Gheychi.App.Theming;
using Gheychi.App.Ui;

namespace Gheychi.App;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeState.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        Platforms.Android.LtrNumbers.Register();
        TextDirection.Register();
        AvaloniaXamlLoader.Load(this);
        RegisterAccentBrushes();
    }

    // The accent is chosen before the app starts (changing it restarts the app), so these are plain brushes.
    private void RegisterAccentBrushes()
    {
        Resources["PrimaryBrush"] = Palette.Accent(AccentRole.Solid);
        Resources["AccentTextBrush"] = Palette.Accent(AccentRole.Text);
        Resources["MintBrush"] = Palette.Accent(AccentRole.Glow);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new MainView();

        base.OnFrameworkInitializationCompleted();
    }
}
