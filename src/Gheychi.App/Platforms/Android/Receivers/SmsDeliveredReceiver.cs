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
            var outcome = ReadOutcome(intent, out var status, out var format);
            switch (outcome)
            {
                case DeliveryOutcome.Delivered:
                    if (SmsDeliveryTally.Delivered(rowId, intent.GetIntExtra(SmsSendTracker.ExtraParts, 1)))
                        AndroidSmsService.MarkDelivered(context, rowId);
                    break;
                case DeliveryOutcome.Failed:
                    var detail = SmsFailureReason.DescribeReportFailure(
                        status, format, intent.GetIntExtra(SmsSendTracker.ExtraPart, 0) + 1, intent.GetIntExtra(SmsSendTracker.ExtraParts, 1));
                    global::Android.Util.Log.Warn(SmsSendTracker.LogTag, $"Delivery failed, row {rowId}: {detail}");
                    SmsFailureStore.Record(context, rowId, status, detail);
                    SmsDeliveryTally.Forget(rowId);
                    AndroidSmsService.MarkDeliveryFailed(context, rowId);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delivered receiver failed: {ex}");
        }
    }

    // A report that the carrier is still trying, or that carries nothing readable, changes nothing: the final one follows.
    private static DeliveryOutcome ReadOutcome(Intent intent, out int status, out string? format)
    {
        status = -1;
        format = intent.GetStringExtra("format");
        var pdu = intent.GetByteArrayExtra("pdu");
        if (pdu is null || pdu.Length == 0)
            return DeliveryOutcome.Pending;

#pragma warning disable CA1422
        var report = format is null ? TelephonySmsMessage.CreateFromPdu(pdu) : TelephonySmsMessage.CreateFromPdu(pdu, format);
#pragma warning restore CA1422
        if (report is null)
            return DeliveryOutcome.Pending;

        status = report.Status;
        return SmsStatusHelper.ClassifyReport(status);
    }
}
