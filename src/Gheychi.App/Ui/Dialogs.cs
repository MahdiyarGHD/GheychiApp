using Android.Content;
using Android.Graphics;
using Gheychi.App.Theming;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace Gheychi.App.Ui;

/// <summary>The system's alert and list dialogs, as MAUI's DisplayAlert and DisplayActionSheet showed them.</summary>
public static class Dialogs
{
    /// <summary>True when the user chose <paramref name="accept"/>. Without a <paramref name="cancel"/> text it is a plain notice.</summary>
    public static Task<bool> AlertAsync(string title, string message, string accept, string? cancel = null)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Platform.CurrentActivity is not { } activity)
            return Task.FromResult(false);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var builder = new AlertDialog.Builder(activity)
                .SetTitle(title)!
                .SetMessage(message)!
                .SetPositiveButton(accept, (_, _) => result.TrySetResult(true))!;
            if (!string.IsNullOrEmpty(cancel))
                builder.SetNegativeButton(cancel, (_, _) => result.TrySetResult(false));

            var dialog = builder.Create()!;
            dialog.DismissEvent += (_, _) => result.TrySetResult(false);
            dialog.Show();
            TintButtons(dialog);
        });
        return result.Task;
    }

    /// <summary>The chosen button's text, <paramref name="cancel"/> when the user backed out, or null for a dismissed sheet.</summary>
    public static Task<string?> ActionSheetAsync(string? title, string cancel, string? destruction, params string[] buttons)
    {
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Platform.CurrentActivity is not { } activity)
            return Task.FromResult<string?>(null);

        var items = new List<string>(buttons);
        if (!string.IsNullOrEmpty(destruction))
            items.Insert(0, destruction);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var builder = new AlertDialog.Builder(activity);
            if (!string.IsNullOrEmpty(title))
                builder.SetTitle(title);
            builder.SetItems(items.ToArray(), (_, args) => result.TrySetResult(items[args.Which]));
            builder.SetNegativeButton(cancel, (_, _) => result.TrySetResult(cancel));

            var dialog = builder.Create()!;
            dialog.DismissEvent += (_, _) => result.TrySetResult(cancel);
            dialog.Show();
            TintButtons(dialog);
        });
        return result.Task;
    }

    // The dialog is the system's, so its buttons take the colour from the theme in colors.xml; the accent is chosen at runtime.
    internal static void TintButtons(AlertDialog dialog)
    {
        var accent = Color.ParseColor(ThemeState.Accent.Hex(AccentRole.Text, ThemeState.IsDark));
        foreach (var which in new[] { -1, -2, -3 })
            dialog.GetButton(which)?.SetTextColor(accent);
    }
}
