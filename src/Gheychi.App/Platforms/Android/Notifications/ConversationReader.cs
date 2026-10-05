using Android.Content;
using Android.Content.PM;
using Android.Provider;
using AndroidX.Core.Content;

namespace Gheychi.App.Platforms.Android.Notifications;

internal readonly record struct UnreadMessage(string Body, long TimestampMillis);

/// <summary>The few provider reads a notification needs. Each is one small indexed query.</summary>
internal static class ConversationReader
{
    public const int MaxUnreadShown = 5;

    private static readonly string UnreadFilter =
        $"{Telephony.Sms.InterfaceConsts.ThreadId} = ? AND {Telephony.Sms.InterfaceConsts.Read} = 0 AND {Telephony.Sms.InterfaceConsts.Type} = {(int)SmsMessageType.Inbox}";

    public static long ReadThreadId(Context context, global::Android.Net.Uri messageRow)
    {
        try
        {
            using var cursor = context.ContentResolver?.Query(
                messageRow, [Telephony.Sms.InterfaceConsts.ThreadId], null, null, null);
            return cursor is not null && cursor.MoveToFirst() ? cursor.GetLong(0) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public static int CountUnread(Context context, long threadId)
    {
        var uri = Telephony.Sms.ContentUri;
        if (uri is null)
            return 0;

        try
        {
            using var cursor = context.ContentResolver?.Query(
                uri,
                [Telephony.Sms.InterfaceConsts.Id],
                UnreadFilter,
                [threadId.ToString()],
                null);
            return cursor?.Count ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Newest unread incoming messages of the thread, oldest first.</summary>
    public static List<UnreadMessage> ReadUnread(Context context, long threadId)
    {
        var result = new List<UnreadMessage>(MaxUnreadShown);
        var uri = Telephony.Sms.ContentUri;
        if (uri is null)
            return result;

        try
        {
            using var cursor = context.ContentResolver?.Query(
                uri,
                [Telephony.Sms.InterfaceConsts.Body, Telephony.Sms.InterfaceConsts.Date],
                UnreadFilter,
                [threadId.ToString()],
                $"{Telephony.Sms.InterfaceConsts.Date} DESC LIMIT {MaxUnreadShown}");

            while (cursor is not null && cursor.MoveToNext())
                result.Add(new UnreadMessage(cursor.GetString(0) ?? string.Empty, cursor.GetLong(1)));
        }
        catch (Exception)
        {
            return result;
        }

        result.Reverse();
        return result;
    }

    /// <summary>Address-book name for the number, or null. Alphanumeric senders are never contacts, so they cost no query.</summary>
    public static string? ReadContactName(Context context, string address)
    {
        if (address.Any(char.IsLetter) || !address.Any(char.IsDigit))
            return null;

        if (ContextCompat.CheckSelfPermission(context, global::Android.Manifest.Permission.ReadContacts) != Permission.Granted)
            return null;

        try
        {
            var filter = ContactsContract.PhoneLookup.ContentFilterUri;
            if (filter is null)
                return null;

            var uri = global::Android.Net.Uri.WithAppendedPath(filter, global::Android.Net.Uri.Encode(address));
            if (uri is null)
                return null;

            using var cursor = context.ContentResolver?.Query(
                uri, [ContactsContract.IContactsColumns.DisplayName], null, null, null);
            return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
