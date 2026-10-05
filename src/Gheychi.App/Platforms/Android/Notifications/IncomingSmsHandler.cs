using Android.Content;
using Android.Provider;
using Gheychi.Core.Notifications;
using Microsoft.Maui.ApplicationModel;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>What happens to a received SMS: store it, tell the open screens, then notify if the rules allow.</summary>
internal static class IncomingSmsHandler
{
    public static void Handle(Context context, string address, string body, long timestampMillis, int subId, Action raiseReceived)
    {
        var threadId = Store(context, address, body, timestampMillis, subId);

        MainThread.BeginInvokeOnMainThread(raiseReceived);

        if (threadId <= 0)
            return;

        try
        {
            var message = new IncomingMessage(threadId, address, body, timestampMillis, subId);
            var decision = IPlatformApplication.Current?.Services.GetService<NotificationPolicy>()?.Decide(message)
                           ?? NotificationDecision.Show;
            if (decision != NotificationDecision.Suppress)
                MessageNotifier.Show(context, message, silent: decision == NotificationDecision.Silent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SMS notification failed: {ex}");
        }
    }

    /// <summary>Writes the message to the inbox and returns its thread id, or 0 when it could not be stored.</summary>
    private static long Store(Context context, string address, string body, long timestampMillis, int subId)
    {
        var values = new ContentValues();
        values.Put(Telephony.Sms.InterfaceConsts.Address, address);
        values.Put(Telephony.Sms.InterfaceConsts.Body, body);
        values.Put(Telephony.Sms.InterfaceConsts.Date, timestampMillis);
        values.Put(Telephony.Sms.InterfaceConsts.Read, 0);
        values.Put(Telephony.Sms.InterfaceConsts.Type, (int)SmsMessageType.Inbox);

        // Without sub_id the message is stored with no SIM, and replying/reacting to it later
        // falls back to SIM 1 — on a dual-SIM phone that sends from the wrong number.
        if (subId > 0)
            values.Put("sub_id", subId);

        try
        {
            var inboxUri = Telephony.Sms.Inbox.ContentUri;
            var row = inboxUri is null ? null : context.ContentResolver?.Insert(inboxUri, values);
            return row is null ? 0 : ConversationReader.ReadThreadId(context, row);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SMS insert failed: {ex}");
            return 0;
        }
    }
}
