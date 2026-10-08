using Android;

// Not in a namespace called Permissions: inside Gheychi.App.Platforms.Android that would hide the Permissions class.
namespace Gheychi.App.Platforms.Android;

public sealed class SmsPermission : BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Manifest.Permission.ReadSms, true),
        (Manifest.Permission.ReceiveSms, true),
        (Manifest.Permission.SendSms, true)
    ];
}

/// <summary>Runtime permission from Android 13; only ask for it there.</summary>
public sealed class NotificationsPermission : BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Manifest.Permission.PostNotifications, true)
    ];
}

public sealed class ContactsPermission : BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Manifest.Permission.ReadContacts, true)
    ];
}
