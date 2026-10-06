using Android.App;
using Android.Content;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.Core.Services;
using TelephonySmsMessage = Android.Telephony.SmsMessage;

namespace Gheychi.App.Platforms.Android.Receivers;

// Declared in the manifest: a delivery report can arrive hours later, long after the process that sent the message is gone.
[BroadcastReceiver(
    Name = "com.evergreen.gheychiapp.SmsDeliveredReceiver",
    Enabled = true,
    Exported = false)]
[IntentFilter([SmsSendTracker.ActionSmsDelivered])]
public sealed class SmsDeliveredReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != SmsSendTracker.ActionSmsDelivered)
            return;

        var rowId = intent.GetLongExtra(SmsSendTracker.ExtraRowId, 0);
        if (rowId <= 0)
            return;

        try
        {
            // A report that the carrier is still trying, or gave up, leaves the message at "sent".
            if (ReachedPhone(intent))
                AndroidSmsService.MarkDelivered(context, rowId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delivered receiver failed: {ex}");
        }
    }

    // TP-Status of the status report: below 32 the message reached the phone, 32-63 the carrier is still
    // trying, 64 and above it gave up.
    private static bool ReachedPhone(Intent intent)
    {
        var pdu = intent.GetByteArrayExtra("pdu");
        if (pdu is null || pdu.Length == 0)
            return false;

        var format = intent.GetStringExtra("format");
#pragma warning disable CA1422
        var report = format is null ? TelephonySmsMessage.CreateFromPdu(pdu) : TelephonySmsMessage.CreateFromPdu(pdu, format);
#pragma warning restore CA1422
        return report is not null && report.Status >= 0 && report.Status < SmsStatusHelper.StatusPending;
    }
}
