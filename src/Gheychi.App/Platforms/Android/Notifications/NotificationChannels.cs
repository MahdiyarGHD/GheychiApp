using Android.App;
using Android.Content;
using Gheychi.App.Localization;

namespace Gheychi.App.Platforms.Android.Notifications;

internal static class NotificationChannels
{
    private static volatile bool _created;

    public static void Ensure(Context context)
    {
        if (_created || !OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            return;

        var loc = LocalizationManager.Instance;
        var channel = new NotificationChannel(
            NotificationIds.ChannelMessages,
            loc["Notification_ChannelMessages"],
            NotificationImportance.High)
        {
            Description = loc["Notification_ChannelMessagesDescription"],
            LockscreenVisibility = NotificationVisibility.Private
        };
        channel.SetShowBadge(true);
        channel.EnableVibration(true);

        manager.CreateNotificationChannel(channel);
        _created = true;
    }
}
