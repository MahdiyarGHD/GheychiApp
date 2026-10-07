using Android.App;
using Android.Content;
using Gheychi.App.Platforms.Android.Notifications;

namespace Gheychi.App.Platforms.Android.Receivers;

/// <summary>The daily spam summary's alarm. Not exported: only this app's own alarm may run it.</summary>
[BroadcastReceiver(Name = "com.evergreen.gheychiapp.SpamDigestReceiver", Exported = false)]
public class SpamDigestReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != SpamDigestNotifier.ActionRun)
            return;

        var pending = GoAsync();
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(async () =>
        {
            try
            {
                await SpamDigestNotifier.RunAsync(appContext);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

/// <summary>Alarms are lost on reboot and set in absolute time, so they are set again after a boot or a clock change.</summary>
[BroadcastReceiver(Name = "com.evergreen.gheychiapp.SpamDigestRescheduleReceiver", Exported = true)]
[IntentFilter([
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced,
    Intent.ActionTimeChanged,
    Intent.ActionTimezoneChanged])]
public class SpamDigestRescheduleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
            SpamDigestNotifier.Schedule(context.ApplicationContext ?? context);
    }
}
