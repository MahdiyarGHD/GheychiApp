using Android.App;
using Android.Content;
using Android.Provider;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.Core.Scheduling;
using AndroidUri = Android.Net.Uri;

namespace Gheychi.App.Platforms.Android.Services;

/// <summary>The alarms behind scheduled and delayed messages: one per message, so each can be cancelled alone.</summary>
public sealed class ScheduledSendAlarm : IScheduledSendScheduler
{
    public const string ActionSend = "com.evergreen.gheychiapp.SEND_SCHEDULED";
    public const string ExtraId = "scheduled_id";

    // A delayed message is normally sent by the chat's own timer; the alarm is only the net under it, so it waits a
    // moment longer and the timer wins when the app is alive.
    private static readonly TimeSpan DelayNetGrace = TimeSpan.FromSeconds(3);

    public bool CanFireExactly =>
        !OperatingSystem.IsAndroidVersionAtLeast(31) ||
        (Platform.AppContext.GetSystemService(Context.AlarmService) as AlarmManager)?.CanScheduleExactAlarms() == true;

    public void Schedule(ScheduledMessage message)
    {
        try
        {
            var context = Platform.AppContext;
            if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms)
                return;

            var at = message.SendAtMillis + (message.IsDelay ? (long)DelayNetGrace.TotalMilliseconds : 0);
            var pending = SendIntent(context, message.Id, create: true)!;
            if (CanFireExactly)
                alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
            else
                alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scheduling a message failed: {ex}");
        }
    }

    public void Cancel(string id)
    {
        try
        {
            var context = Platform.AppContext;
            if (context.GetSystemService(Context.AlarmService) is AlarmManager alarms && SendIntent(context, id, create: false) is { } pending)
            {
                alarms.Cancel(pending);
                pending.Cancel();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cancelling a scheduled message failed: {ex}");
        }
    }

    /// <summary>Alarms are lost on reboot, so every waiting message gets its alarm again; one already due fires at once.</summary>
    public static void RescheduleAll(Context context)
    {
        var services = IPlatformApplication.Current?.Services;
        var store = services?.GetService<IScheduledSendStore>();
        var scheduler = services?.GetService<IScheduledSendScheduler>();
        if (store is null || scheduler is null)
            return;

        foreach (var message in store.GetAll())
            scheduler.Schedule(message);
    }

    /// <summary>The system screen where the user lets the app fire at exact times (Android 12 and later).</summary>
    public static void OpenExactAlarmSettings(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            return;

        var intent = new Intent(Settings.ActionRequestScheduleExactAlarm, AndroidUri.Parse("package:" + context.PackageName))
            .AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    // The id is in the data so every message has an intent of its own; extras alone do not tell two pending intents apart.
    private static PendingIntent? SendIntent(Context context, string id, bool create)
    {
        var intent = new Intent(context, typeof(ScheduledSendReceiver))
            .SetAction(ActionSend)
            .SetData(AndroidUri.Parse("gheychi://scheduled/" + id))
            .PutExtra(ExtraId, id);
        var flags = PendingIntentFlags.Immutable | (create ? PendingIntentFlags.UpdateCurrent : PendingIntentFlags.NoCreate);
        return PendingIntent.GetBroadcast(context, 0, intent, flags);
    }
}
