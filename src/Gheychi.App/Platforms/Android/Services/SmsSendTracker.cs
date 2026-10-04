using Android.App;
using Android.Content;
using Android.Telephony;

namespace Gheychi.App.Platforms.Android.Services;

/// <summary>
/// Hands messages to the radio and reports when every part has been accepted. The result is a task
/// that is never abandoned on a timer: a slow send can still go out after the caller stops
/// waiting, and the row has to follow what really happened.
/// </summary>
internal static class SmsSendTracker
{
    public const string ActionSmsSent = "com.evergreen.gheychiapp.SMS_SENT";
    public const string ExtraToken = "com.evergreen.gheychiapp.SMS_TOKEN";
    public const string ExtraRowId = "com.evergreen.gheychiapp.SMS_ROW_ID";

    /// <summary>Raised when a stored outgoing message reaches its final state (rowId, success).</summary>
    public static event Action<long, bool>? OutgoingUpdated;

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

    public static void RaiseOutgoingUpdated(long rowId, bool ok)
    {
        try
        {
            OutgoingUpdated?.Invoke(rowId, ok);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OutgoingUpdated handler failed: {ex}");
        }
    }

    public static PendingIntent? CreateSentIntent(Context context, string token, long rowId)
    {
        try
        {
            var intent = new Intent(ActionSmsSent);
            intent.SetPackage(context.PackageName);
            intent.PutExtra(ExtraToken, token);
            intent.PutExtra(ExtraRowId, rowId);
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

    /// <summary>Completes when the radio has reported every part; false if any part failed or could not be queued.</summary>
    public static Task<bool> SendAsync(
        SmsManager smsManager, string address, string text, IList<string>? parts, Context context, long rowId)
    {
        var multipart = parts != null && parts.Count > 1;
        var token = Register(multipart ? parts!.Count : 1);

        try
        {
            if (multipart)
            {
                var sentIntents = new List<PendingIntent>(parts!.Count);
                foreach (var _ in parts)
                {
                    var sentIntent = CreateSentIntent(context, token, rowId);
                    if (sentIntent == null)
                    {
                        Abandon(token);
                        return Task.FromResult(false);
                    }
                    sentIntents.Add(sentIntent);
                }

                smsManager.SendMultipartTextMessage(address, null, parts, sentIntents, null);
            }
            else
            {
                var sentIntent = CreateSentIntent(context, token, rowId);
                if (sentIntent == null)
                {
                    Abandon(token);
                    return Task.FromResult(false);
                }

                smsManager.SendTextMessage(address, null, text, sentIntent, null);
            }
        }
        catch
        {
            Abandon(token);
            return Task.FromResult(false);
        }

        lock (PendingLock)
        {
            return Pending.TryGetValue(token, out var send) ? send.Tcs.Task : Task.FromResult(false);
        }
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

    /// <summary>Returns false when nothing in this process is waiting for the token (the app was restarted since the send).</summary>
    public static bool CompletePart(string token, bool ok)
    {
        lock (PendingLock)
        {
            if (!Pending.TryGetValue(token, out var send))
                return false;

            send.Received++;
            send.AllOk &= ok;
            if (send.Received >= send.Expected)
            {
                Pending.Remove(token);
                send.Tcs.TrySetResult(send.AllOk);
            }

            return true;
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
}
