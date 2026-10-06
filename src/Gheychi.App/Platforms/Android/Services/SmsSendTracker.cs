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
    public const string ActionSmsDelivered = "com.evergreen.gheychiapp.SMS_DELIVERED";
    public const string ExtraToken = "com.evergreen.gheychiapp.SMS_TOKEN";
    public const string ExtraRowId = "com.evergreen.gheychiapp.SMS_ROW_ID";

    /// <summary>Raised when the carrier accepted or rejected a stored outgoing message (rowId, success).</summary>
    public static event Action<long, bool>? OutgoingUpdated;

    /// <summary>Raised when a delivery report confirmed a stored outgoing message reached the phone (rowId, threadId).</summary>
    public static event Action<long, long>? Delivered;

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

    public static void RaiseDelivered(long rowId, long threadId)
    {
        try
        {
            Delivered?.Invoke(rowId, threadId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delivered handler failed: {ex}");
        }
    }

    private static PendingIntent? CreateDeliveryIntent(Context context, long rowId)
    {
        if (rowId <= 0)
            return null;

        try
        {
            var intent = new Intent(ActionSmsDelivered);
            intent.SetPackage(context.PackageName);
            intent.PutExtra(ExtraRowId, rowId);
            var requestCode = Interlocked.Increment(ref _requestCodeSeed);
            // Mutable: the platform adds the status report ("pdu", "format") to this intent.
            return PendingIntent.GetBroadcast(
                context,
                requestCode,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);
        }
        catch
        {
            return null;
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
        var deliveryIntent = CreateDeliveryIntent(context, rowId);

        try
        {
            if (multipart)
            {
                var sentIntents = new List<PendingIntent>(parts!.Count);
                // Only the last part asks for a report: it is the one that completes the message on the phone.
                var deliveryIntents = new List<PendingIntent?>(parts.Count);
                foreach (var _ in parts)
                {
                    var sentIntent = CreateSentIntent(context, token, rowId);
                    if (sentIntent == null)
                    {
                        Abandon(token);
                        return Task.FromResult(false);
                    }
                    sentIntents.Add(sentIntent);
                    deliveryIntents.Add(deliveryIntents.Count == parts.Count - 1 ? deliveryIntent : null);
                }

                smsManager.SendMultipartTextMessage(address, null, parts, sentIntents, deliveryIntent is null ? null : deliveryIntents!);
            }
            else
            {
                var sentIntent = CreateSentIntent(context, token, rowId);
                if (sentIntent == null)
                {
                    Abandon(token);
                    return Task.FromResult(false);
                }

                smsManager.SendTextMessage(address, null, text, sentIntent, deliveryIntent);
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
