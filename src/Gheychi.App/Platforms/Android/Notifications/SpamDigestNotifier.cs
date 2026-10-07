using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Gheychi.App.Controls.Settings;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.App.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// Spam is quarantined without a notification, so once a day a silent one says how much was caught and opens the
/// Spam tab. It is skipped on days with nothing new, and replaced rather than stacked.
/// </summary>
internal static class SpamDigestNotifier
{
    public const string ActionRun = "com.evergreen.gheychiapp.SPAM_DIGEST";

    /// <summary>Puts the next run on the alarm clock, or takes it off when the summary or the spam filter is off.</summary>
    public static void Schedule(Context context)
    {
        try
        {
            if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms || RunIntent(context) is not { } pending)
                return;

            var spamFilterOn = IPlatformApplication.Current?.Services.GetService<ISpamSettings>()?.Enabled ?? true;
            if (!SpamDigestPreferences.Enabled || !spamFilterOn)
            {
                alarms.Cancel(pending);
                return;
            }

            var next = SpamDigests.NextRun(DateTime.Now, SpamDigestPreferences.MinuteOfDay, SpamDigestPreferences.LastRun);
            var at = new DateTimeOffset(next).ToUnixTimeMilliseconds();
            // Inexact on purpose: exact alarms need a permission the user has to grant, and a few minutes late is fine.
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scheduling the spam summary failed: {ex}");
        }
    }

    public static async Task RunAsync(Context context)
    {
        var now = DateTime.Now;
        var since = SpamDigests.WindowStart(now, SpamDigestPreferences.LastRun);
        // Recorded first: a run that fails part-way must not repeat in a loop of catch-up alarms.
        SpamDigestPreferences.LastRun = now;

        try
        {
            if (SpamDigestPreferences.Enabled
                && IPlatformApplication.Current?.Services.GetService<ISpamMessageRepository>() is { } repository
                && SpamDigests.Compose(await repository.GetAllAsync(), since, now, SpamDigestPreferences.SeenAt) is { } digest)
                Show(context, digest);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spam summary failed: {ex}");
        }
        finally
        {
            Schedule(context);
        }
    }

    /// <summary>The user opened the Spam tab: the summary has nothing left to tell. Call off the UI thread.</summary>
    public static void MarkSeen(Context context)
    {
        SpamDigestPreferences.SeenAt = DateTime.Now;
        NotificationManagerCompat.From(context)?.Cancel(NotificationIds.SpamDigestId);
    }

    private static void Show(Context context, SpamDigest digest)
    {
        NotificationChannels.EnsureSpamDigest(context);
        var loc = LocalizationManager.Instance;
        var count = SettingsUi.Number(digest.Count);
        var title = (digest.AllToday, digest.Count == 1) switch
        {
            (true, true) => loc["Spam_DigestTitleTodayOne"],
            (true, false) => string.Format(loc["Spam_DigestTitleToday"], count),
            (false, true) => loc["Spam_DigestTitleOne"],
            (false, false) => string.Format(loc["Spam_DigestTitle"], count)
        };
        var senders = digest.TopSenders.Select(LtrNumbers.Wrap).ToList();
        var text = (senders.Count, digest.OtherSenders) switch
        {
            (1, _) => string.Format(loc["Spam_DigestFromOne"], senders[0]),
            (_, 0) => string.Format(loc["Spam_DigestFromTwo"], senders[0], senders[1]),
            _ => string.Format(loc["Spam_DigestFromMany"], senders[0], senders[1], SettingsUi.Number(digest.OtherSenders))
        };

        var builder = new NotificationCompat.Builder(context, NotificationIds.ChannelSpamDigest);
        builder.SetSmallIcon(MessageNotifier.SmallIcon(context));
        builder.SetColor(MessageNotifier.AccentColor);
        builder.SetContentTitle(title);
        builder.SetContentText(text);
        builder.SetCategory(NotificationCompat.CategoryStatus);
        builder.SetPriority(NotificationCompat.PriorityLow);
        builder.SetSilent(true);
        builder.SetShowWhen(true);
        builder.SetAutoCancel(true);
        // Its own group, or Android would fold it into the conversations' summary.
        builder.SetGroup(NotificationIds.SpamDigestGroupKey);
        builder.SetContentIntent(OpenSpamTabIntent(context));
        builder.SetVisibility(NotificationCompat.VisibilityPrivate);
        builder.SetPublicVersion(new NotificationCompat.Builder(context, NotificationIds.ChannelSpamDigest)
            .SetSmallIcon(MessageNotifier.SmallIcon(context))!
            .SetColor(MessageNotifier.AccentColor)!
            .SetContentTitle(title)!
            .Build());

        if (builder.Build() is { } notification)
            NotificationManagerCompat.From(context)?.Notify(NotificationIds.SpamDigestId, notification);
    }

    private static PendingIntent? RunIntent(Context context)
    {
        var intent = new Intent(context, typeof(SpamDigestReceiver));
        intent.SetAction(ActionRun);
        return PendingIntent.GetBroadcast(context, NotificationIds.SpamDigestId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private static PendingIntent? OpenSpamTabIntent(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra(NotificationIds.ExtraOpenSpamTab, true);
        return PendingIntent.GetActivity(context, NotificationIds.SpamDigestId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }
}

/// <summary>A tapped spam summary asks for the Spam tab; kept until the shell is up to show it, as with chats.</summary>
public static class SpamTabRequests
{
    private static int _pending;

    public static event Action? Requested;

    public static void FromIntent(Intent? intent)
    {
        if (intent?.GetBooleanExtra(NotificationIds.ExtraOpenSpamTab, false) != true)
            return;

        // The intent is delivered again after the activity is restored; it must not switch tabs again.
        intent.RemoveExtra(NotificationIds.ExtraOpenSpamTab);
        Volatile.Write(ref _pending, 1);
        if (ChatPresence.IsAppVisible)
            Requested?.Invoke();
    }

    public static void RaiseIfPending()
    {
        if (Volatile.Read(ref _pending) == 1)
            Requested?.Invoke();
    }

    /// <summary>True once per request.</summary>
    public static bool Take() => Interlocked.Exchange(ref _pending, 0) == 1;
}
