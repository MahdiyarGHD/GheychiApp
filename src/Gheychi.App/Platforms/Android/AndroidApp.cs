using Android.App;
using Android.Runtime;
using AndroidX.AppCompat.App;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.Platforms.Android.Notifications;

namespace Gheychi.App;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        builder = base.CustomizeAppBuilder(builder);

        var fallbacks = new List<FontFallback>();
        if (AppFonts.PersianLetters is { } persian)
            fallbacks.Add(persian);
        if (SystemEmoji.Fallbacks is { } emoji)
        {
            fallbacks.AddRange(emoji);
            builder = builder.AfterSetup(_ => Task.Run(SystemEmoji.Register));
        }

        return fallbacks.Count > 0 ? builder.With(new FontManagerOptions { FontFallbacks = fallbacks.ToArray() }) : builder;
    }

    // The services are built here and not when the first screen is: a text that arrives while the app is closed
    // starts only this process (a receiver), with no activity and so no Avalonia window.
    public override void OnCreate()
    {
        CultureService.ApplyCulture();
        ThemeState.Resolve();
        // The system's dialogs (delete confirms, pickers) follow the app's theme choice, not the phone's.
        AppCompatDelegate.DefaultNightMode = Services.AppPreferences.Theme switch
        {
            AppTheme.Light => AppCompatDelegate.ModeNightNo,
            AppTheme.Dark => AppCompatDelegate.ModeNightYes,
            _ => AppCompatDelegate.ModeNightFollowSystem
        };
        SQLitePCL.Batteries_V2.Init();
        IPlatformApplication.Current = new PlatformApplication(AppServices.Build());

        base.OnCreate();

        // The alarm is gone after a force stop, and an app update can leave it unset.
        _ = Task.Run(() => SpamDigestNotifier.Schedule(Platform.AppContext));
    }
}
