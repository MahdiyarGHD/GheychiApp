using Android.Content;
using Android.OS;
using Android.Widget;
using Gheychi.App.Localization;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// Puts a one-time code on the clipboard, where the keyboard offers it above the keys and any field can paste it. The
/// code goes nowhere else: it is not logged, stored or sent.
/// </summary>
internal static class OtpClipboard
{
    // ClipDescription.EXTRA_IS_SENSITIVE: the system keeps the text out of its "copied" preview (Android 13 and later).
    private const string SensitiveExtra = "android.content.extra.IS_SENSITIVE";

    /// <param name="sensitive">Hides the code from the system's preview of the clipboard: for a copy the user did not ask for.</param>
    public static bool Copy(Context context, string code, bool sensitive)
    {
        try
        {
            if (context.GetSystemService(Context.ClipboardService) is not ClipboardManager manager)
                return false;

            var clip = ClipData.NewPlainText(LocalizationManager.Instance["Notification_CodeLabel"], code);
            if (sensitive && OperatingSystem.IsAndroidVersionAtLeast(33) && clip?.Description is { } description)
            {
                var extras = new PersistableBundle();
                extras.PutBoolean(SensitiveExtra, true);
                description.Extras = extras;
            }

            manager.PrimaryClip = clip;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Copying a code failed: {ex}");
            return false;
        }
    }

    /// <summary>Copies the code and says so: from Android 13 on the system does that itself.</summary>
    public static bool CopyWithFeedback(Context context, string code, bool sensitive)
    {
        if (!Copy(context, code, sensitive))
            return false;

        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            MainThread.BeginInvokeOnMainThread(() =>
                Toast.MakeText(context, LocalizationManager.Instance["Notification_CodeCopied"], ToastLength.Short)?.Show());
        }

        return true;
    }
}
