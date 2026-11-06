using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.Core.Updates;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>Says a new app version is out, once per version; tapped, it opens Settings > About to install it.</summary>
internal static class UpdateNotifier
{
    public static void Show(Context context, AppRelease release)
    {
        if (AppPreferences.UpdateNotifiedTag == release.Tag)
            return;
        AppPreferences.UpdateNotifiedTag = release.Tag;

        NotificationChannels.EnsureUpdates(context);
        var loc = LocalizationManager.Instance;
        var builder = new NotificationCompat.Builder(context, NotificationIds.ChannelUpdates);
        builder.SetSmallIcon(MessageNotifier.SmallIcon(context));
        builder.SetColor(MessageNotifier.AccentColor);
        builder.SetContentTitle(string.Format(loc["Notification_UpdateTitle"], LocalizedNumbers.Digits(release.Tag.TrimStart('v', 'V'))));
        builder.SetContentText(loc["Notification_UpdateText"]);
        builder.SetCategory(NotificationCompat.CategoryRecommendation);
        builder.SetPriority(NotificationCompat.PriorityLow);
        builder.SetSilent(true);
        builder.SetAutoCancel(true);
        // Its own group, or Android would fold it into the conversations' summary.
        builder.SetGroup(NotificationIds.UpdateGroupKey);
        builder.SetContentIntent(OpenAboutIntent(context));

        if (builder.Build() is { } notification)
            NotificationManagerCompat.From(context)?.Notify(NotificationIds.UpdateId, notification);
    }

    public static void Cancel(Context context) => NotificationManagerCompat.From(context)?.Cancel(NotificationIds.UpdateId);

    private static PendingIntent? OpenAboutIntent(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra(NotificationIds.ExtraOpenAbout, true);
        return PendingIntent.GetActivity(context, NotificationIds.UpdateId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }
}

/// <summary>A tapped update notification asks for Settings > About; kept until the shell is up to show it.</summary>
public static class AboutScreenRequests
{
    private static int _pending;

    public static void FromIntent(Intent? intent)
    {
        if (intent?.GetBooleanExtra(NotificationIds.ExtraOpenAbout, false) != true)
            return;

        // The intent is delivered again after the activity is restored; it must not open the screen again.
        intent.RemoveExtra(NotificationIds.ExtraOpenAbout);
        Volatile.Write(ref _pending, 1);
    }

    /// <summary>True once per request.</summary>
    public static bool Take() => Interlocked.Exchange(ref _pending, 0) == 1;
}
