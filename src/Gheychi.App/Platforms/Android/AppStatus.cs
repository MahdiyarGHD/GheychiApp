using Android.Content;
using AndroidX.Core.App;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Platforms.Android.Permissions;
using Microsoft.Maui.ApplicationModel;

namespace Gheychi.App.Platforms.Android;

/// <summary>What the app is allowed to do on this phone, and asking for what is missing.</summary>
internal static class AppStatus
{
    /// <summary>Covers both the Android 13 permission and the user turning the app's notifications off.</summary>
    public static bool NotificationsEnabled() => NotificationManagerCompat.From(Platform.AppContext).AreNotificationsEnabled();

    public static async Task<bool> ContactsAllowedAsync() =>
        await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<ContactsPermission>() == PermissionStatus.Granted;

    // When Android will not show its prompt any more (denied twice, or turned off by hand), the system settings are opened instead.
    public static async Task RequestNotificationsAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<NotificationsPermission>() != PermissionStatus.Granted)
        {
            await Task.Run(() => NotificationChannels.Ensure(Platform.AppContext));
            if (await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<NotificationsPermission>() == PermissionStatus.Granted
                || Microsoft.Maui.ApplicationModel.Permissions.ShouldShowRationale<NotificationsPermission>())
                return;
        }

        if (!NotificationsEnabled())
            NotificationChannels.OpenAppSettings(Platform.AppContext);
    }

    public static async Task RequestContactsAsync()
    {
        if (await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<ContactsPermission>() != PermissionStatus.Granted
            && !Microsoft.Maui.ApplicationModel.Permissions.ShouldShowRationale<ContactsPermission>())
            AppInfo.Current.ShowSettingsUI();
    }

    /// <summary>Starts the app again in a new process, so everything is built in the newly chosen language.</summary>
    public static void Restart()
    {
        var context = Platform.AppContext;
        var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (intent is null)
            return;

        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        context.StartActivity(intent);
        Java.Lang.JavaSystem.Exit(0);
    }
}
