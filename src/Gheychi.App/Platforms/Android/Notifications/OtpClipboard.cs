using Android.Content;
using Android.Widget;
using Gheychi.App.Localization;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// Puts a one-time code on the clipboard, where the keyboard offers it above the keys and any field can paste it. The
/// code goes nowhere else: it is not logged, stored or sent.
/// </summary>
internal static class OtpClipboard
{
    // Not marked sensitive (ClipDescription.EXTRA_IS_SENSITIVE): on a Xiaomi phone the dynamic password copied on
    // arrival, marked so, never reached the clipboard, while the same code copied unmarked from the notification did.
    public static bool Copy(Context context, string code)
    {
        try
        {
            if (context.GetSystemService(Context.ClipboardService) is not ClipboardManager manager)
                return false;

            manager.PrimaryClip = ClipData.NewPlainText(LocalizationManager.Instance["Notification_CodeLabel"], code);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Copying a code failed: {ex}");
            return false;
        }
    }

    /// <summary>Copies the code and says so: from Android 13 on the system does that itself.</summary>
    public static bool CopyWithFeedback(Context context, string code)
    {
        if (!Copy(context, code))
            return false;

        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            MainThread.BeginInvokeOnMainThread(() =>
                Toast.MakeText(context, LocalizationManager.Instance["Notification_CodeCopied"], ToastLength.Short)?.Show());
        }

        return true;
    }
}
