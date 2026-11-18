using Android.App;
using Android.Runtime;
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
        builder = Ui.TextLineFixes.Register(base.CustomizeAppBuilder(builder))
            // The held first draw (MainActivity) keeps the main queue busy, so Avalonia's input marker cannot run; after
            // the default 1 s its renders drop to input priority and a slow launch's first frame waits for the deadline.
            .With(new Avalonia.Threading.DispatcherOptions { InputStarvationTimeout = TimeSpan.FromSeconds(3) });

        var fallbacks = new List<FontFallback>();
        if (AppFonts.PersianLetters is { } persian)
            fallbacks.Add(persian);
        if (SystemEmoji.Fallbacks is { } emoji)
        {
            fallbacks.AddRange(emoji);
            builder = builder.AfterSetup(_ =>
            {
                // Made here first: FontManager.Current creates and registers itself without a lock, and the worker
                // below racing the UI thread for it could lose the emoji collection.
                _ = FontManager.Current;
                _ = Task.Run(SystemEmoji.Register);
            });
        }

        return fallbacks.Count > 0 ? builder.With(new FontManagerOptions { FontFallbacks = fallbacks.ToArray() }) : builder;
    }

    // The services are built here and not when the first screen is: a text that arrives while the app is closed
    // starts only this process (a receiver), with no activity and so no Avalonia window.
    public override void OnCreate()
    {
        Services.StartupTiming.Mark("app.oncreate");
        WarmUpNativeGraphics();
        CultureService.ApplyCulture();
        ThemeState.Resolve();
        // The system's dialogs (delete confirms, pickers) and the launch window follow the app's theme choice, not the phone's.
        ThemeState.ApplyToSystem(Services.AppPreferences.Theme);
        SQLitePCL.Batteries_V2.Init();
        IPlatformApplication.Current = new PlatformApplication(AppServices.Build());
        Services.StartupTiming.Mark("services");

        base.OnCreate();
        Services.StartupTiming.Mark("avalonia.setup");
        // Not before base.OnCreate: the UI thread's dispatcher must exist before anything on a worker could touch one.
        _ = Task.Run(WarmUpInboxServices);

        _ = ScheduleAlarmsAsync();
    }

    // Native set-up Avalonia's setup otherwise does on the UI thread: loading Skia and its Android font manager (which
    // reads fonts.xml and opens every system font), HarfBuzz, and the GPU driver behind the EGL display. Started first,
    // on workers, it overlaps the services; the UI thread then finds it done. Through the same entry points Avalonia
    // uses (SKFontManager.Default, eglInitialize on the default display, which a second caller gets at once), and the
    // display is never terminated.
    private static void WarmUpNativeGraphics()
    {
        _ = Task.Run(() =>
        {
            Services.StartupTiming.Mark("warm.skia.start");
            _ = SkiaSharp.SKFontManager.Default;
            using (new HarfBuzzSharp.Buffer())
            {
            }
            Services.StartupTiming.Mark("warm.skia.end");
        });
        _ = Task.Run(() =>
        {
            Services.StartupTiming.Mark("warm.egl.start");
            var display = global::Android.Opengl.EGL14.EglGetDisplay(global::Android.Opengl.EGL14.EglDefaultDisplay);
            var ok = display is not null && global::Android.Opengl.EGL14.EglInitialize(display, new int[1], 0, new int[1], 0);
            Services.StartupTiming.Mark(ok ? "warm.egl.end" : "warm.egl.failed");
        });
    }

    // The inbox's services read preferences and JSON, and the first .resx lookup loads the Persian satellite assembly;
    // the UI thread builds the inbox right after this and then only picks them up.
    private static void WarmUpInboxServices()
    {
        try
        {
            if (IPlatformApplication.Current?.Services is not { } services)
                return;

            services.GetService<Gheychi.Core.Services.ISmsService>();
            services.GetService<Gheychi.Core.Services.IDraftStore>();
            services.GetService<Gheychi.Core.Services.IPinStore>();
            services.GetService<Services.PreferencesFolderStore>();
            _ = LocalizationManager.Instance["Chat_Copied"];
            Services.StartupTiming.Mark("warm.inbox.end");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warming the inbox services failed: {ex}");
        }
    }

    // The alarm is gone after a force stop, and an app update can leave it unset. Setting them reads the database and
    // talks to the alarm service, which took CPU from the first frame: when the app is being opened (its activity is
    // created right after this) they wait. A process started for a text has no activity and sets them at once.
    private static async Task ScheduleAlarmsAsync()
    {
        await Task.Delay(ActivityCheckDelay).ConfigureAwait(false);
        if (Platform.CurrentActivity is not null)
            await Task.Delay(OpeningDelay).ConfigureAwait(false);

        _ = Task.Run(() => SpamDigestNotifier.Schedule(Platform.AppContext));
        _ = Task.Run(() => Gheychi.App.Platforms.Android.Services.ScheduledSendAlarm.RescheduleAll(Platform.AppContext));
        _ = Task.Run(() => Gheychi.App.Platforms.Android.Services.UpdateCheckJob.Schedule(Platform.AppContext));
    }

    private static readonly TimeSpan ActivityCheckDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan OpeningDelay = TimeSpan.FromSeconds(5);
}
