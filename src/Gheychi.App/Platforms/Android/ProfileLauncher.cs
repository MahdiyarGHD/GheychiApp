using Android.Content;
using Android.Content.PM;
using Android.Provider;
using AndroidX.Core.Content;
using Gheychi.App.Platforms.Android.Notifications;
using Microsoft.Maui.ApplicationModel;
using AndroidUri = global::Android.Net.Uri;

namespace Gheychi.App.Platforms.Android;

/// <summary>The system screens the profile page hands over to: dialer, contact card, per-number notification settings.</summary>
internal static class ProfileLauncher
{
    private static Context Context => Platform.CurrentActivity ?? Platform.AppContext;

    /// <summary>Opens the dialer with the number filled in; the user presses call, so no permission is needed.</summary>
    public static void Dial(string address)
    {
        Start(new Intent(Intent.ActionDial, AndroidUri.Parse("tel:" + AndroidUri.Encode(address))));
    }

    /// <summary>Shows the address-book card for the number, or the new-contact form when it is not saved yet.</summary>
    public static void ShowContact(string address)
    {
        var contactUri = FindContactUri(address);
        if (contactUri is not null)
        {
            Start(new Intent(Intent.ActionView, contactUri));
            return;
        }

        var insert = new Intent(ContactsContract.Intents.Insert.Action);
        insert.SetType(ContactsContract.RawContacts.ContentType);
        insert.PutExtra(ContactsContract.Intents.Insert.Phone, address);
        Start(insert);
    }

    public static void OpenNotificationSettings(string address, string displayName) =>
        NotificationChannels.OpenSettings(Context, address, displayName);

    public static bool HasCustomNotifications(string address) =>
        NotificationChannels.HasConversationChannel(Context, address);

    private static AndroidUri? FindContactUri(string address)
    {
        if (ContextCompat.CheckSelfPermission(Context, global::Android.Manifest.Permission.ReadContacts) != Permission.Granted)
            return null;

        try
        {
            var filter = ContactsContract.PhoneLookup.ContentFilterUri;
            var lookup = filter is null ? null : AndroidUri.WithAppendedPath(filter, AndroidUri.Encode(address));
            if (lookup is null)
                return null;

            using var cursor = Context.ContentResolver?.Query(
                lookup,
                [IBaseColumns.Id, ContactsContract.IContactsColumns.LookupKey],
                null, null, null);
            if (cursor is null || !cursor.MoveToFirst())
                return null;

            return ContactsContract.Contacts.GetLookupUri(cursor.GetLong(0), cursor.GetString(1));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Contact lookup failed: {ex}");
            return null;
        }
    }

    private static void Start(Intent intent)
    {
        try
        {
            intent.AddFlags(ActivityFlags.NewTask);
            Context.StartActivity(intent);
        }
        catch (ActivityNotFoundException ex)
        {
            System.Diagnostics.Debug.WriteLine($"No app to handle {intent.Action}: {ex.Message}");
        }
    }
}
