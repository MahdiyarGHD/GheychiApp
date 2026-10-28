using Android.Content;
using Gheychi.Core.Services;

namespace Gheychi.App.Platforms.Android.Services;

/// <summary>
/// Remembers why an outgoing message failed, for the message's Info sheet. The first failure of a message is kept (later
/// parts usually fail for the same reason); the result code also goes into the provider row's error_code column. Only the
/// last few messages are kept. Written synchronously: the receiver's process may be gone right after it.
/// </summary>
internal static class SmsFailureStore
{
    private const int Cap = 200;
    private const string OrderKey = "_order";
    private static readonly object Gate = new();

    private static ISharedPreferences Store =>
        global::Android.App.Application.Context.GetSharedPreferences("gheychi_send_failures", FileCreationMode.Private)!;

    /// <summary>Keeps <paramref name="detail"/> as the reason for the row unless it already has one.</summary>
    public static void Record(Context context, long rowId, int resultCode, string detail)
    {
        if (rowId <= 0)
            return;

        try
        {
            lock (Gate)
            {
                var store = Store;
                var key = KeyOf(rowId);
                if (store.Contains(key))
                    return;

                var order = SmsFailureReason.Remember(store.GetString(OrderKey, null), rowId, Cap, out var evicted);
                var editor = store.Edit()!;
                editor.PutString(key, detail);
                editor.PutString(OrderKey, order);
                foreach (var old in evicted)
                    editor.Remove(KeyOf(old));
                editor.Commit();
            }

            if (resultCode != 0)
                AndroidSmsService.SetErrorCode(context, rowId, resultCode);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn(SmsSendTracker.LogTag, $"Could not keep the failure reason: {ex.Message}");
        }
    }

    /// <summary>Why the message failed; null when nothing was recorded (sent by another app, or too long ago).</summary>
    public static string? Get(long rowId)
    {
        try
        {
            lock (Gate)
                return Store.GetString(KeyOf(rowId), null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The message is going out again: whatever failed before no longer applies.</summary>
    public static void Clear(long rowId)
    {
        try
        {
            lock (Gate)
            {
                var store = Store;
                var key = KeyOf(rowId);
                if (store.Contains(key))
                    store.Edit()!.Remove(key)!.Commit();
            }
        }
        catch
        {
        }
    }

    private static string KeyOf(long rowId) => "r" + rowId;
}
