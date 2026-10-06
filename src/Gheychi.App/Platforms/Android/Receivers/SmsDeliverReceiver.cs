using Android.App;
using Android.Content;
using Android.Provider;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.Core.Spam;

namespace Gheychi.App.Platforms.Android.Receivers;

[BroadcastReceiver(
    Name = "com.evergreen.gheychiapp.SmsDeliverReceiver",
    Permission = "android.permission.BROADCAST_SMS",
    Exported = true)]
[IntentFilter(["android.provider.Telephony.SMS_DELIVER"])]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public static event Action<long>? SmsReceived;

    /// <summary>A message was quarantined as spam. Raised on the main thread.</summary>
    public static event Action<SpamMessage>? SpamReceived;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null || intent.Action != "android.provider.Telephony.SMS_DELIVER")
            return;

        var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
        if (messages is null || messages.Length == 0)
            return;

        var first = messages.FirstOrDefault(m => m is not null);
        if (first is null)
            return;

        // A long SMS arrives as several PDUs in one intent; they are one message.
        var body = string.Concat(messages.Where(m => m is not null).Select(m => m.DisplayMessageBody));
        var address = first.DisplayOriginatingAddress ?? string.Empty;
        var timestamp = first.TimestampMillis;
        var subId = ReadSubscriptionId(intent);

        // The provider write and the notification queries stay off the main thread. goAsync keeps the
        // process alive until they finish, without a service or a wake lock.
        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                await IncomingSmsHandler.HandleAsync(context, address, body, timestamp, subId, RaiseSmsReceived, RaiseSpamReceived);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SMS handling failed: {ex}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    // Runs on the main thread: a subscriber exception here would kill the app.
    internal static void RaiseSmsReceived(long threadId)
    {
        try
        {
            SmsReceived?.Invoke(threadId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SmsReceived handler failed: {ex}");
        }
    }

    private static void RaiseSpamReceived(SpamMessage message)
    {
        try
        {
            SpamReceived?.Invoke(message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SpamReceived handler failed: {ex}");
        }
    }

    private static int ReadSubscriptionId(Intent intent)
    {
        var subId = intent.GetIntExtra("subscription", -1);
        if (subId <= 0)
            subId = intent.GetIntExtra("android.telephony.extra.SUBSCRIPTION_INDEX", -1);
        return subId;
    }
}
