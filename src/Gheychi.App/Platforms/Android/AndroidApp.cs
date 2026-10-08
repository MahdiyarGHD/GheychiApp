using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Notifications;

namespace Gheychi.App;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    // The services are built here and not when the first screen is: a text that arrives while the app is closed
    // starts only this process (a receiver), with no activity and so no Avalonia window.
    public override void OnCreate()
    {
        CultureService.ApplyCulture();
        ThemeState.Resolve();
        SQLitePCL.Batteries_V2.Init();
        IPlatformApplication.Current = new PlatformApplication(AppServices.Build());

        base.OnCreate();

        // The alarm is gone after a force stop, and an app update can leave it unset.
        _ = Task.Run(() => SpamDigestNotifier.Schedule(Platform.AppContext));
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).UseSkia().UseHarfBuzz();
}
