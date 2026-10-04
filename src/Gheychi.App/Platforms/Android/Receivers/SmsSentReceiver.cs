using Android.App;
using Android.Content;
using Gheychi.App.Platforms.Android.Services;

namespace Gheychi.App.Platforms.Android.Receivers;

public sealed class SmsSentReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != SmsSendTracker.ActionSmsSent)
            return;

        var token = intent.GetStringExtra(SmsSendTracker.ExtraToken);
        if (string.IsNullOrEmpty(token))
            return;

        SmsSendTracker.CompletePart(token, ResultCode == Result.Ok);
    }
}
