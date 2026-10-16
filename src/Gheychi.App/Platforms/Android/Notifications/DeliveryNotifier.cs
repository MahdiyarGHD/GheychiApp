using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.Core.Services;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// Tells the user, when they asked for it, that a message they sent reached the phone or that the carrier gave up on it.
/// One notification per message, so a later report for another message never replaces it.
/// </summary>
internal static class DeliveryNotifier
{
    private const int PreviewLength = 80;

    /// <summary>Safe to call whenever a report settles a message: it does nothing while the setting is off.</summary>
    public static void Show(Context context, long rowId, bool delivered)
    {
        try
        {
            if (!AppPreferences.DeliveryNotifications)
                return;

            var manager = NotificationManagerCompat.From(context);
            if (!manager.AreNotificationsEnabled() || ReadMessage(context, rowId) is not { } message)
                return;

            NotificationChannels.EnsureDelivery(context);
            var loc = LocalizationManager.Instance;
            var name = ConversationReader.ReadContactName(context, message.Address)
                       ?? (PhoneNumberNormalizer.IsAlphanumeric(message.Address) ? message.Address : PhoneNumberNormalizer.FormatDisplay(message.Address));
            var shownName = LtrNumbers.Wrap(name);
            var title = string.Format(loc[delivered ? "Notification_DeliveredTo" : "Notification_NotDeliveredTo"], shownName);

            var builder = new NotificationCompat.Builder(context, NotificationIds.ChannelDelivery);
            builder.SetSmallIcon(MessageNotifier.SmallIcon(context));
            builder.SetColor(MessageNotifier.AccentColor);
            builder.SetContentTitle(title);
            builder.SetContentText(Preview(message.Body));
            builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(message.Body));
            builder.SetCategory(NotificationCompat.CategoryStatus);
            builder.SetShowWhen(true);
            builder.SetAutoCancel(true);
            // Its own group, or Android would fold it into the conversations' summary.
            builder.SetGroup(NotificationIds.DeliveryGroupKey);
            builder.SetContentIntent(MessageNotifier.OpenIntent(context, message.ThreadId, message.Address, name, message.SubId));
            builder.SetVisibility(NotificationCompat.VisibilityPrivate);
            builder.SetPublicVersion(new NotificationCompat.Builder(context, NotificationIds.ChannelDelivery)
                .SetSmallIcon(MessageNotifier.SmallIcon(context))!
                .SetColor(MessageNotifier.AccentColor)!
                .SetContentTitle(loc[delivered ? "Notification_DeliveredHidden" : "Notification_NotDeliveredHidden"])!
                .Build());

            if (builder.Build() is { } notification)
                manager.Notify(NotificationIds.DeliveryTag, (int)(rowId & int.MaxValue), notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delivery notification failed: {ex}");
        }
    }

    private static string Preview(string body)
    {
        var line = body.ReplaceLineEndings(" ").Trim();
        return line.Length <= PreviewLength ? line : line[..PreviewLength].TrimEnd() + "…";
    }

    private readonly record struct SentMessage(string Address, string Body, long ThreadId, int SubId);

    private static SentMessage? ReadMessage(Context context, long rowId)
    {
        if (global::Android.Provider.Telephony.Sms.ContentUri is not { } smsUri)
            return null;

        using var cursor = context.ContentResolver?.Query(
            ContentUris.WithAppendedId(smsUri, rowId), ["address", "body", "thread_id", "sub_id"], null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
            return null;

        var address = cursor.GetString(0);
        return string.IsNullOrEmpty(address)
            ? null
            : new SentMessage(address, cursor.GetString(1) ?? string.Empty, cursor.GetLong(2), cursor.IsNull(3) ? -1 : cursor.GetInt(3));
    }
}
