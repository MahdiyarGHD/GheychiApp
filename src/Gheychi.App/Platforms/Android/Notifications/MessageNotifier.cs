using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.Core.Notifications;
using Gheychi.Core.Services;
using AndroidXPerson = AndroidX.Core.App.Person;
using AndroidXRemoteInput = AndroidX.Core.App.RemoteInput;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// One notification per conversation, rebuilt from the provider's unread messages so it is right even
/// when the process was killed since the last message. The conversations are grouped under a summary.
/// </summary>
internal static class MessageNotifier
{
    public const string ReplyKey = "reply_text";

    private const int MinConversationsForSummary = 2;

    private static readonly int AccentColor = global::Android.Graphics.Color.ParseColor("#2E6B4C").ToArgb();
    private static int _smallIcon;

    /// <summary>A new message arrived: alert, unless <paramref name="silent"/>.</summary>
    public static void Show(Context context, IncomingMessage message, bool silent) =>
        Post(context, message.ThreadId, message.Address, message.SubId,
            new UnreadMessage(message.Body, message.TimestampMillis), silent, onlyAlertOnce: false);

    /// <summary>Re-posts the conversation without alerting, e.g. to end an inline reply's progress spinner.</summary>
    public static void Refresh(Context context, long threadId, string address, int subId) =>
        Post(context, threadId, address, subId, incoming: null, silent: false, onlyAlertOnce: true);

    /// <summary>Dismisses the conversation's notification. Talks to the notification service: call it off the UI thread.</summary>
    public static void Cancel(Context context, long threadId)
    {
        try
        {
            var id = NotificationIds.ForThread(threadId);
            var active = ActiveConversationIds(context);
            if (!active.Remove(id))
                return;

            var manager = NotificationManagerCompat.From(context);
            manager.Cancel(id);

            // A group of one is just that notification; a lone summary would leave an empty header behind.
            if (active.Count < MinConversationsForSummary)
                manager.Cancel(NotificationIds.SummaryId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cancel notification failed: {ex}");
        }
    }

    private static HashSet<int> ActiveConversationIds(Context context)
    {
        var ids = new HashSet<int>();
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
            return ids;

        foreach (var active in manager.GetActiveNotifications() ?? [])
        {
            if (active.Id != NotificationIds.SummaryId && active.Notification?.Group == NotificationIds.GroupKey)
                ids.Add(active.Id);
        }

        return ids;
    }

    private static void Post(
        Context context, long threadId, string address, int subId, UnreadMessage? incoming, bool silent, bool onlyAlertOnce)
    {
        var manager = NotificationManagerCompat.From(context);
        if (!manager.AreNotificationsEnabled())
            return;

        var unread = ConversationReader.ReadUnread(context, threadId);
        if (unread.Count == 0 && incoming is { } fallback)
            unread.Add(fallback);

        var id = NotificationIds.ForThread(threadId);
        if (unread.Count == 0)
        {
            Cancel(context, threadId);
            return;
        }

        NotificationChannels.Ensure(context);

        var name = ConversationReader.ReadContactName(context, address)
                   ?? (PhoneNumberNormalizer.IsAlphanumeric(address) ? address : PhoneNumberNormalizer.FormatDisplay(address));
        var last = unread[^1];

        var builder = new NotificationCompat.Builder(context, NotificationIds.ChannelMessages);
        builder.SetSmallIcon(SmallIcon(context));
        builder.SetColor(AccentColor);
        builder.SetContentTitle(name);
        builder.SetContentText(last.Body);
        builder.SetStyle(BuildStyle(name, address, unread));
        builder.SetCategory(NotificationCompat.CategoryMessage);
        builder.SetPriority(NotificationCompat.PriorityHigh);
        builder.SetDefaults(NotificationCompat.DefaultAll);
        builder.SetWhen(last.TimestampMillis);
        builder.SetShowWhen(true);
        builder.SetAutoCancel(true);
        builder.SetGroup(NotificationIds.GroupKey);
        builder.SetOnlyAlertOnce(onlyAlertOnce);
        builder.SetSilent(silent);
        builder.SetContentIntent(OpenIntent(context, threadId, address, name, subId));
        builder.AddAction(BuildReplyAction(context, threadId, address, subId));
        builder.AddAction(BuildMarkReadAction(context, threadId, address, subId));

        var notification = builder.Build();
        if (notification is null)
            return;

        var conversations = ActiveConversationIds(context);
        conversations.Add(id);

        manager.Notify(id, notification);
        if (conversations.Count >= MinConversationsForSummary)
            manager.Notify(NotificationIds.SummaryId, BuildSummary(context, conversations.Count));
    }

    private static NotificationCompat.MessagingStyle BuildStyle(string name, string address, List<UnreadMessage> unread)
    {
        var me = new AndroidXPerson.Builder().SetName(LocalizationManager.Instance["Notification_You"])!.Build()!;
        var sender = new AndroidXPerson.Builder().SetName(name)!.SetKey(address)!.Build()!;

        var style = new NotificationCompat.MessagingStyle(me);
        foreach (var message in unread)
            style.AddMessage(new NotificationCompat.MessagingStyle.Message(message.Body, message.TimestampMillis, sender));
        return style;
    }

    private static Notification BuildSummary(Context context, int conversations)
    {
        var loc = LocalizationManager.Instance;
        var summary = new NotificationCompat.Builder(context, NotificationIds.ChannelMessages);
        summary.SetSmallIcon(SmallIcon(context));
        summary.SetColor(AccentColor);
        summary.SetContentTitle(loc["Notification_ChannelMessages"]);
        summary.SetContentText(string.Format(loc["Notification_ConversationCount"], conversations));
        summary.SetContentIntent(OpenAppIntent(context));
        summary.SetGroup(NotificationIds.GroupKey);
        summary.SetGroupSummary(true);
        summary.SetGroupAlertBehavior(NotificationCompat.GroupAlertChildren);
        summary.SetAutoCancel(true);
        return summary.Build()!;
    }

    private static PendingIntent? OpenAppIntent(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        return PendingIntent.GetActivity(
            context, NotificationIds.SummaryId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private static PendingIntent? OpenIntent(Context context, long threadId, string address, string name, int subId)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra(NotificationIds.ExtraThreadId, threadId);
        intent.PutExtra(NotificationIds.ExtraAddress, address);
        intent.PutExtra(NotificationIds.ExtraName, name);
        intent.PutExtra(NotificationIds.ExtraSubId, subId);
        return PendingIntent.GetActivity(
            context,
            NotificationIds.RequestCode(threadId, NotificationIds.PendingSlot.Open),
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private static NotificationCompat.Action BuildMarkReadAction(Context context, long threadId, string address, int subId)
    {
        var pending = PendingIntent.GetBroadcast(
            context,
            NotificationIds.RequestCode(threadId, NotificationIds.PendingSlot.MarkRead),
            ActionIntent(context, NotificationActionReceiver.ActionMarkRead, threadId, address, subId),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var action = new NotificationCompat.Action.Builder(0, LocalizationManager.Instance["Messages_MarkAsRead"], pending);
        action.SetSemanticAction(NotificationCompat.Action.SemanticActionMarkAsRead);
        action.SetShowsUserInterface(false);
        return action.Build()!;
    }

    private static NotificationCompat.Action BuildReplyAction(Context context, long threadId, string address, int subId)
    {
        var label = LocalizationManager.Instance["Notification_Reply"];
        var remoteInput = new AndroidXRemoteInput.Builder(ReplyKey).SetLabel(label)!.Build()!;

        // The system fills the typed text into this intent, so it has to be mutable from Android 12 on.
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            flags |= PendingIntentFlags.Mutable;

        var pending = PendingIntent.GetBroadcast(
            context,
            NotificationIds.RequestCode(threadId, NotificationIds.PendingSlot.Reply),
            ActionIntent(context, NotificationActionReceiver.ActionReply, threadId, address, subId),
            flags);

        var action = new NotificationCompat.Action.Builder(0, label, pending);
        action.AddRemoteInput(remoteInput);
        action.SetAllowGeneratedReplies(true);
        action.SetSemanticAction(NotificationCompat.Action.SemanticActionReply);
        action.SetShowsUserInterface(false);
        return action.Build()!;
    }

    private static Intent ActionIntent(Context context, string action, long threadId, string address, int subId)
    {
        var intent = new Intent(context, typeof(NotificationActionReceiver));
        intent.SetAction(action);
        intent.PutExtra(NotificationIds.ExtraThreadId, threadId);
        intent.PutExtra(NotificationIds.ExtraAddress, address);
        intent.PutExtra(NotificationIds.ExtraSubId, subId);
        return intent;
    }

    private static int SmallIcon(Context context)
    {
        if (_smallIcon == 0)
            _smallIcon = context.Resources?.GetIdentifier("ic_stat_message", "drawable", context.PackageName) ?? 0;

        return _smallIcon != 0 ? _smallIcon : context.ApplicationInfo?.Icon ?? 0;
    }
}
