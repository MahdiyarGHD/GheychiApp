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

        foreach (var msg in messages)
        {
            if (msg is null)
                continue;

            var values = new ContentValues();
            values.Put(Telephony.Sms.InterfaceConsts.Address, msg.DisplayOriginatingAddress);
            values.Put(Telephony.Sms.InterfaceConsts.Body, msg.DisplayMessageBody);
            values.Put(Telephony.Sms.InterfaceConsts.Date, msg.TimestampMillis);
            values.Put(Telephony.Sms.InterfaceConsts.Read, 0);
            values.Put(Telephony.Sms.InterfaceConsts.Type, (int)SmsMessageType.Inbox);

            var inboxUri = Telephony.Sms.Inbox.ContentUri;
            if (inboxUri != null)
                context.ContentResolver?.Insert(inboxUri, values);
        }

        SmsReceived?.Invoke();
    }
}
