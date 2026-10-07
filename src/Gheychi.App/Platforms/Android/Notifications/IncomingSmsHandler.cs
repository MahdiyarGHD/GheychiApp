using Android.Content;
using Android.Provider;
using Gheychi.Core.Notifications;
using Gheychi.Core.Spam;
using Microsoft.Maui.ApplicationModel;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>What happens to a received SMS: quarantine it if it is spam; otherwise store it, tell the open screens, then notify if the rules allow.</summary>
internal static class IncomingSmsHandler
{
    private static readonly TimeSpan SpamCheckBudget = TimeSpan.FromSeconds(4);

    /// <param name="sentMillis">When the sender's carrier says it was sent (the PDU timestamp).</param>
    public static async Task HandleAsync(Context context, string address, string body, long sentMillis, int subId,
        Action<long> raiseReceived, Action<SpamMessage> raiseSpamReceived)
    {
        // Ordered by arrival, as the stock messaging app does: the carrier's clock can be minutes or hours off, which
        // put a new message before older ones and made the notification show an older message as the latest.
        var timestampMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var spam = await QuarantineIfSpamAsync(context, address, body, timestampMillis, subId);
        if (spam is not null)
        {
            MainThread.BeginInvokeOnMainThread(() => raiseSpamReceived(spam));
            return;
        }

        var threadId = Store(context, address, body, timestampMillis, subId, sentMillis: sentMillis);

        MainThread.BeginInvokeOnMainThread(() => raiseReceived(threadId));

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

    /// <summary>
    /// Spam goes only to the app's own database, never to the system SMS store, so it cannot show up in the inbox,
    /// a conversation, search or a notification. Null when the message is not spam or could not be quarantined.
    /// </summary>
    private static async Task<SpamMessage?> QuarantineIfSpamAsync(Context context, string address, string body, long timestampMillis, int subId)
    {
        try
        {
            var services = IPlatformApplication.Current?.Services;
            var detector = services?.GetService<SpamDetector>();
            var repository = services?.GetService<ISpamMessageRepository>();
            if (detector is null || repository is null)
                return null;

            var fromContact = ConversationReader.ReadContactName(context, address) is not null;
            var detection = detector.DetectAsync(address, body, fromContact);

            // Android kills the process when the receiver overruns its time, and an SMS not stored by then is lost.
            // When the model is still loading (first message after a cold start) the message goes to the inbox
            // instead; the load carries on, so the next message is checked.
            if (await Task.WhenAny(detection, Task.Delay(SpamCheckBudget)) != detection)
            {
                System.Diagnostics.Debug.WriteLine("Spam check overran its budget; the message goes to the inbox.");
                return null;
            }

            if (await detection is not { } verdict)
                return null;

            var message = new SpamMessage(0, address, body,
                DateTimeOffset.FromUnixTimeMilliseconds(timestampMillis).LocalDateTime, subId,
                verdict.Score.Probability, verdict.Score.ModelVersion, verdict.Threshold);
            var id = await repository.AddAsync(message);
            if (id <= 0)
                return null;

            if (services?.GetService<ISpamStatsRepository>() is { } stats)
                await stats.AddAsync(SpamStats.From(message with { Id = id }));
            return message with { Id = id };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spam check failed: {ex}");
            return null;
        }
    }

    /// <summary>Writes the message to the inbox and returns its thread id, or 0 when it could not be stored.</summary>
    internal static long Store(Context context, string address, string body, long timestampMillis, int subId, bool read = false, long sentMillis = 0)
    {
        var values = new ContentValues();
        values.Put(Telephony.Sms.InterfaceConsts.Address, address);
        values.Put(Telephony.Sms.InterfaceConsts.Body, body);
        values.Put(Telephony.Sms.InterfaceConsts.Date, timestampMillis);
        if (sentMillis > 0)
            values.Put(Telephony.Sms.InterfaceConsts.DateSent, sentMillis);
        values.Put(Telephony.Sms.InterfaceConsts.Read, read ? 1 : 0);
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
