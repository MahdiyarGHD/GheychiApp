using Android.App;
using Android.Content;
using Android.Provider;

namespace Gheychi.App.Platforms.Android.Receivers;

[BroadcastReceiver(
    Name = "com.evergreen.gheychiapp.SmsDeliverReceiver",
    Permission = "android.permission.BROADCAST_SMS",
    Exported = true)]
[IntentFilter(["android.provider.Telephony.SMS_DELIVER"])]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public static event Action? SmsReceived;

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

        var values = new ContentValues();
        values.Put(Telephony.Sms.InterfaceConsts.Address, first.DisplayOriginatingAddress);
        values.Put(Telephony.Sms.InterfaceConsts.Body, body);
        values.Put(Telephony.Sms.InterfaceConsts.Date, first.TimestampMillis);
        values.Put(Telephony.Sms.InterfaceConsts.Read, 0);
        values.Put(Telephony.Sms.InterfaceConsts.Type, (int)SmsMessageType.Inbox);

        // Without sub_id the message is stored with no SIM, and replying/reacting to it later
        // falls back to SIM 1 — on a dual-SIM phone that sends from the wrong number.
        var subId = ReadSubscriptionId(intent);
        if (subId > 0)
            values.Put("sub_id", subId);

        var inboxUri = Telephony.Sms.Inbox.ContentUri;
        if (inboxUri != null)
            context.ContentResolver?.Insert(inboxUri, values);

        SmsReceived?.Invoke();
    }

    private static int ReadSubscriptionId(Intent intent)
    {
        var subId = intent.GetIntExtra("subscription", -1);
        if (subId <= 0)
            subId = intent.GetIntExtra("android.telephony.extra.SUBSCRIPTION_INDEX", -1);
        return subId;
    }
}
