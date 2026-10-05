using Android.App;
using Android.Content;
using Android.Provider;
using Gheychi.App.Localization;
using Gheychi.Core.Services;

namespace Gheychi.App.Platforms.Android.Notifications;

internal static class NotificationChannels
{
    private const string ConversationPrefix = "chat_";

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

    /// <summary>
    /// The channel a conversation's notifications go to: its own once the user has asked to manage it, otherwise the
    /// shared one. Conversations only get a channel on request, so the system settings are not flooded with one per sender.
    /// </summary>
    public static string ChannelFor(Context context, string address)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return NotificationIds.ChannelMessages;

        var id = ConversationChannelId(address);
        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        return manager?.GetNotificationChannel(id) is null ? NotificationIds.ChannelMessages : id;
    }

    public static bool HasConversationChannel(Context context, string address)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return false;

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        return manager?.GetNotificationChannel(ConversationChannelId(address)) is not null;
    }

    /// <summary>Opens the system's notification settings for this number: sound, vibration, lights, importance.</summary>
    public static void OpenSettings(Context context, string address, string displayName)
    {
        Intent intent;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            Ensure(context);
            var id = EnsureConversationChannel(context, address, displayName);
            intent = new Intent(Settings.ActionChannelNotificationSettings);
            intent.PutExtra(Settings.ExtraChannelId, id);
        }
        else
        {
            intent = new Intent(Settings.ActionAppNotificationSettings);
        }

        intent.PutExtra(Settings.ExtraAppPackage, context.PackageName);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    private static string EnsureConversationChannel(Context context, string address, string displayName)
    {
        var id = ConversationChannelId(address);
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            return id;

        // Creating an existing channel only refreshes its name; what the user changed in the settings is kept.
        var channel = new NotificationChannel(id, string.IsNullOrWhiteSpace(displayName) ? address : displayName, NotificationImportance.High)
        {
            LockscreenVisibility = NotificationVisibility.Private
        };
        channel.SetShowBadge(true);
        channel.EnableVibration(true);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            channel.SetConversationId(NotificationIds.ChannelMessages, ConversationKey(address));

        manager.CreateNotificationChannel(channel);
        return id;
    }

    private static string ConversationKey(string address) => PhoneNumberNormalizer.ToLookupKey(address);

    private static string ConversationChannelId(string address) => ConversationPrefix + ConversationKey(address);
}
