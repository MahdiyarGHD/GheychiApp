using Android;
using Microsoft.Maui.ApplicationModel;

namespace Gheychi.App.Platforms.Android.Permissions;

public sealed class SmsPermission : Microsoft.Maui.ApplicationModel.Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Manifest.Permission.ReadSms, true),
        (Manifest.Permission.ReceiveSms, true),
        (Manifest.Permission.SendSms, true)
    ];
}

public sealed class ContactsPermission : Microsoft.Maui.ApplicationModel.Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        (Manifest.Permission.ReadContacts, true)
    ];
}
