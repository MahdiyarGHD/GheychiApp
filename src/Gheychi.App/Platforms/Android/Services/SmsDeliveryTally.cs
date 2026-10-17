using Android.Content;

namespace Gheychi.App.Platforms.Android.Services;

/// <summary>
/// A long message goes out in parts and each part is reported on its own, perhaps hours apart and across restarts of the
/// process. The count is kept on disk, and written synchronously: the receiver's process may be gone right after it.
/// </summary>
internal static class SmsDeliveryTally
{
    private static readonly object Gate = new();

    private static ISharedPreferences Store =>
        global::Android.App.Application.Context.GetSharedPreferences("gheychi_delivery_tally", FileCreationMode.Private)!;

    /// <summary>Counts one delivered part; true when that was the last one the message was waiting for.</summary>
    public static bool Delivered(long rowId, int parts)
    {
        if (parts <= 1)
            return true;

        lock (Gate)
        {
            var key = rowId.ToString();
            var store = Store;
            var count = store.GetInt(key, 0) + 1;
            var editor = store.Edit()!;
            if (count >= parts)
                editor.Remove(key);
            else
                editor.PutInt(key, count);
            editor.Commit();
            return count >= parts;
        }
    }

    /// <summary>A part failed: the message will not be delivered, and the parts still to come count for nothing.</summary>
    public static void Forget(long rowId)
    {
        lock (Gate)
            Store.Edit()!.Remove(rowId.ToString())!.Commit();
    }
}
