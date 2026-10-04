using Android.App;
using Android.Content;
using Gheychi.App.Platforms.Android.Services;

namespace Gheychi.App.Platforms.Android.Receivers;

// Declared in the manifest (not registered at runtime) so a send that finishes after the process
// was killed still reaches us and the stored message does not stay "sending" forever.
[BroadcastReceiver(
    Name = "com.evergreen.gheychiapp.SmsSentReceiver",
    Enabled = true,
    Exported = false)]
[IntentFilter(["com.evergreen.gheychiapp.SMS_SENT"])]
public sealed class SmsSentReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != SmsSendTracker.ActionSmsSent)
            return;

        var token = intent.GetStringExtra(SmsSendTracker.ExtraToken);
        if (string.IsNullOrEmpty(token))
            return;

        try
        {
            var ok = ResultCode == Result.Ok;

            // Same process as the sender: its continuation updates the row once all parts are in.
            if (SmsSendTracker.CompletePart(token, ok))
                return;

            // Process was restarted after the send; there is no waiter, so update the row from
            // this single part (any failed part marks the message failed).
            var rowId = intent.GetLongExtra(SmsSendTracker.ExtraRowId, 0);
            if (rowId > 0)
                AndroidSmsService.UpdateOutgoingState(context, rowId, ok, onlyIfPending: ok);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sent receiver failed: {ex}");
        }
    }
}
