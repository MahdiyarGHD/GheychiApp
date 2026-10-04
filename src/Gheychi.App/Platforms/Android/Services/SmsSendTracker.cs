using Android.App;
using Android.Content;
using Android.Telephony;
using Gheychi.App.Platforms.Android.Receivers;

namespace Gheychi.App.Platforms.Android.Services;

internal static class SmsSendTracker
{
    public const string ActionSmsSent = "com.evergreen.gheychiapp.SMS_SENT";
    public const string ExtraToken = "com.evergreen.gheychiapp.SMS_TOKEN";

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(60);

    private static readonly SmsSentReceiver Receiver = new();
    private static bool _registered;
    private static readonly object RegLock = new();
    private static int _requestCodeSeed = Random.Shared.Next(1 << 20, 1 << 30);

    private sealed class PendingSend
    {
        public int Expected;
        public int Received;
        public bool AllOk = true;
        public readonly TaskCompletionSource<bool> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly Dictionary<string, PendingSend> Pending = [];
    private static readonly object PendingLock = new();

    public static void EnsureRegistered(Context context)
    {
        lock (RegLock)
        {
            if (_registered)
                return;

            var filter = new IntentFilter(ActionSmsSent);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
#pragma warning disable CA1422
                context.RegisterReceiver(Receiver, filter, ReceiverFlags.NotExported);
#pragma warning restore CA1422
            }
            else
            {
#pragma warning disable CS0618
                context.RegisterReceiver(Receiver, filter);
#pragma warning restore CS0618
            }

            _registered = true;
        }
    }

    public static PendingIntent? CreateSentIntent(Context context, string token)
    {
        try
        {
            var intent = new Intent(ActionSmsSent);
            intent.SetPackage(context.PackageName);
            intent.PutExtra(ExtraToken, token);
            var requestCode = Interlocked.Increment(ref _requestCodeSeed);
            return PendingIntent.GetBroadcast(
                context,
                requestCode,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> SendSingleAsync(
        SmsManager smsManager, string address, string text, Context context, CancellationToken cancellationToken)
    {
        var token = Register(parts: 1);
        var sentIntent = CreateSentIntent(context, token);
        if (sentIntent == null)
        {
            Abandon(token);
            return false;
        }

        try
        {
            smsManager.SendTextMessage(address, null, text, sentIntent, null);
        }
        catch
        {
            Abandon(token);
            return false;
        }

        return await WaitAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<bool> SendMultipartAsync(
        SmsManager smsManager, string address, IList<string> parts, Context context, CancellationToken cancellationToken)
    {
        var token = Register(parts.Count);
        var sentIntents = new List<PendingIntent>(parts.Count);
        foreach (var _ in parts)
        {
            var sentIntent = CreateSentIntent(context, token);
            if (sentIntent == null)
            {
                Abandon(token);
                return false;
            }
            sentIntents.Add(sentIntent);
        }

        try
        {
            smsManager.SendMultipartTextMessage(address, null, parts, sentIntents, null);
        }
        catch
        {
            Abandon(token);
            return false;
        }

        return await WaitAsync(token, cancellationToken).ConfigureAwait(false);
    }

    private static string Register(int parts)
    {
        var token = Guid.NewGuid().ToString("N");
        lock (PendingLock)
        {
            Pending[token] = new PendingSend { Expected = Math.Max(1, parts) };
        }
        return token;
    }

    public static void CompletePart(string token, bool ok)
    {
        lock (PendingLock)
        {
            if (!Pending.TryGetValue(token, out var send))
                return;

            send.Received++;
            send.AllOk &= ok;
            if (send.Received >= send.Expected)
            {
                Pending.Remove(token);
                send.Tcs.TrySetResult(send.AllOk);
            }
        }
    }

    private static void Abandon(string token)
    {
        lock (PendingLock)
        {
            if (Pending.Remove(token, out var send))
                send.Tcs.TrySetResult(false);
        }
    }

    private static async Task<bool> WaitAsync(string token, CancellationToken cancellationToken)
    {
        PendingSend? send;
        lock (PendingLock)
        {
            Pending.TryGetValue(token, out send);
        }

        if (send == null)
            return false;

        using var timeoutCts = new CancellationTokenSource(SendTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await send.Tcs.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            return send.Tcs.Task.Result;
        }
        catch (OperationCanceledException)
        {
            Abandon(token);
            return false;
        }
    }
}
