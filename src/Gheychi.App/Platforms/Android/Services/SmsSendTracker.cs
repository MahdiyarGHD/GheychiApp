using Android.App;
using Android.Content;
using Android.Telephony;
using Gheychi.Core.Services;

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
    public const string ExtraParts = "com.evergreen.gheychiapp.SMS_PARTS";
    public const string ExtraPart = "com.evergreen.gheychiapp.SMS_PART";

    /// <summary>Tag of the log lines that say why a part failed: <c>adb logcat -s Gheychi.Sms</c>.</summary>
    public const string LogTag = "Gheychi.Sms";

    /// <summary>Raised when the carrier accepted or rejected a stored outgoing message (rowId, threadId, success).</summary>
    public static event Action<long, long, bool>? OutgoingUpdated;

    /// <summary>Raised when a delivery report confirmed a stored outgoing message reached the phone (rowId, threadId).</summary>
    public static event Action<long, long>? Delivered;

    /// <summary>Raised when a delivery report said the carrier gave up on a stored outgoing message (rowId, threadId).</summary>
    public static event Action<long, long>? DeliveryFailed;

    private static int _requestCodeSeed = Random.Shared.Next(1 << 20, 1 << 30);

    private sealed class PendingSend
    {
        public required SmsPartTally Tally;
        public readonly TaskCompletionSource<bool> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly Dictionary<string, PendingSend> Pending = [];
    private static readonly object PendingLock = new();

    public static void RaiseOutgoingUpdated(long rowId, long threadId, bool ok)
    {
        try
        {
            OutgoingUpdated?.Invoke(rowId, threadId, ok);
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

    public static void RaiseDeliveryFailed(long rowId, long threadId)
    {
        try
        {
            DeliveryFailed?.Invoke(rowId, threadId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeliveryFailed handler failed: {ex}");
        }
    }

    private static PendingIntent? CreateDeliveryIntent(Context context, long rowId, int part, int parts)
    {
        if (rowId <= 0)
            return null;

        try
        {
            var intent = new Intent(ActionSmsDelivered);
            intent.SetPackage(context.PackageName);
            intent.PutExtra(ExtraRowId, rowId);
            intent.PutExtra(ExtraPart, part);
            intent.PutExtra(ExtraParts, parts);
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

    public static PendingIntent? CreateSentIntent(Context context, string token, long rowId, int part, int parts)
    {
        try
        {
            var intent = new Intent(ActionSmsSent);
            intent.SetPackage(context.PackageName);
            intent.PutExtra(ExtraToken, token);
            intent.PutExtra(ExtraRowId, rowId);
            intent.PutExtra(ExtraPart, part);
            intent.PutExtra(ExtraParts, parts);
            var requestCode = Interlocked.Increment(ref _requestCodeSeed);
            // Mutable: the platform adds the radio's own cause ("errorCode") to this intent, which says why a part failed.
            return PendingIntent.GetBroadcast(
                context,
                requestCode,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(LogTag, $"Could not create the sent intent: {ex}");
            return null;
        }
    }

    /// <summary>Completes when the radio has reported every part; false if any part failed or could not be queued.</summary>
    public static Task<bool> SendAsync(
        SmsManager smsManager, string address, string text, IList<string>? parts, Context context, long rowId)
    {
        var multipart = parts != null && parts.Count > 1;
        var token = Register(multipart ? parts!.Count : 1, out var pendingSend);

        try
        {
            if (multipart)
            {
                var sentIntents = new List<PendingIntent>(parts!.Count);
                // Every part asks for its own report: the message is delivered only when all of them are.
                var deliveryIntents = new List<PendingIntent?>(parts.Count);
                for (var i = 0; i < parts.Count; i++)
                {
                    var sentIntent = CreateSentIntent(context, token, rowId, i, parts.Count);
                    if (sentIntent == null)
                    {
                        Abandon(token);
                        return Task.FromResult(false);
                    }
                    sentIntents.Add(sentIntent);
                    deliveryIntents.Add(CreateDeliveryIntent(context, rowId, i, parts.Count));
                }

                smsManager.SendMultipartTextMessage(address, null, parts, sentIntents, rowId > 0 && deliveryIntents.All(i => i is not null) ? deliveryIntents! : null);
            }
            else
            {
                var sentIntent = CreateSentIntent(context, token, rowId, 0, 1);
                if (sentIntent == null)
                {
                    Abandon(token);
                    return Task.FromResult(false);
                }

                smsManager.SendTextMessage(address, null, text, sentIntent, CreateDeliveryIntent(context, rowId, 0, 1));
            }
        }
        catch (Exception ex)
        {
            // The platform refused the send outright (bad number, no permission): no broadcast will follow, so this is the only trace.
            global::Android.Util.Log.Warn(LogTag, $"Send threw for row {rowId}: {ex}");
            SmsFailureStore.Record(context, rowId, 0, $"Send threw: {ex.GetType().Name}: {ex.Message}");
            Abandon(token);
            return Task.FromResult(false);
        }

        // Held from before the send: the radio can answer for every part before this point is reached, and
        // looking the token up again would then find nothing and report a message that went out as failed.
        return pendingSend.Tcs.Task;
    }

    private static string Register(int parts, out PendingSend send)
    {
        var token = Guid.NewGuid().ToString("N");
        send = new PendingSend { Tally = new SmsPartTally(parts) };
        lock (PendingLock)
        {
            Pending[token] = send;
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

            if (send.Tally.Record(ok))
            {
                Pending.Remove(token);
                send.Tcs.TrySetResult(send.Tally.AllOk);
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
