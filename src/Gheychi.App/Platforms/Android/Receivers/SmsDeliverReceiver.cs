using Android.App;
using Android.Content;
using Android.Provider;
using Gheychi.App.Platforms.Android.Notifications;

namespace Gheychi.App.Platforms.Android.Receivers;

[BroadcastReceiver(
    Name = "com.evergreen.gheychiapp.SmsDeliverReceiver",
    Permission = "android.permission.BROADCAST_SMS",
    Exported = true)]
[IntentFilter(["android.provider.Telephony.SMS_DELIVER"])]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public static event Action<long>? SmsReceived;

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
        Task.Run(() =>
        {
            try
            {
                IncomingSmsHandler.Handle(context, address, body, timestamp, subId, RaiseSmsReceived);
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
    private static void RaiseSmsReceived(long threadId)
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

    private static int ReadSubscriptionId(Intent intent)
    {
        var subId = intent.GetIntExtra("subscription", -1);
        if (subId <= 0)
            subId = intent.GetIntExtra("android.telephony.extra.SUBSCRIPTION_INDEX", -1);
        return subId;
    }
}
