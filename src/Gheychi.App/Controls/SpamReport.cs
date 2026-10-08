using Android.Text;
using Android.Views;
using Android.Widget;
using Gheychi.App.Localization;
using Gheychi.App.Ui;
using Gheychi.Core.Spam;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace Gheychi.App.Controls;

/// <summary>A user's correction of the spam detector: from the Spam tab ("not spam") or a chat ("spam").</summary>
internal static class SpamReport
{
    /// <summary>
    /// Shows the message in an editable box so the user can take out anything personal before it leaves the phone,
    /// then sends what is left in the background and tells the user how it went. False when the user cancelled.
    /// </summary>
    public static async Task<bool> SubmitAsync(string body, bool isSpam)
    {
        if (string.IsNullOrWhiteSpace(body) || IPlatformApplication.Current?.Services.GetService<ISpamReporter>() is not { } reporter)
            return false;

        string? text;
        try
        {
            // Called from async void tap handlers: nothing here may throw.
            text = await EditAsync(body, isSpam);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Showing the spam report dialog failed: {ex}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
            return false;

        _ = SendAsync(reporter, text, isSpam);
        return true;
    }

    /// <summary>The text to send, or null when the user cancelled.</summary>
    private static Task<string?> EditAsync(string body, bool isSpam)
    {
        if (Platform.CurrentActivity is not { } activity)
            return Task.FromResult<string?>(null);

        var loc = LocalizationManager.Instance;
        var density = activity.Resources?.DisplayMetrics?.Density ?? 1;
        var result = new TaskCompletionSource<string?>();

        var input = new EditText(activity)
        {
            Text = body,
            InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine | InputTypes.TextFlagNoSuggestions,
            Gravity = GravityFlags.Top | GravityFlags.Start,
            VerticalScrollBarEnabled = true
        };
        input.SetMinLines(3);
        input.SetMaxLines(8);
        input.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);

        var frame = new FrameLayout(activity);
        var side = (int)(22 * density);
        frame.SetPadding(side, (int)(4 * density), side, 0);
        frame.AddView(input, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var dialog = new AlertDialog.Builder(activity)
            .SetTitle(loc[isSpam ? "Spam_ReportSpamConfirmTitle" : "Spam_ReportHamConfirmTitle"])!
            .SetMessage(loc["Spam_ReportConfirmMessage"])!
            .SetView(frame)!
            .SetPositiveButton(loc["Spam_ReportConfirmSend"], (_, _) => result.TrySetResult(input.Text))!
            .SetNegativeButton(loc["Chat_Cancel"], (_, _) => result.TrySetResult(null))!
            .Create();
        dialog.DismissEvent += (_, _) => result.TrySetResult(null);
        dialog.Show();

        // Nothing to send once the user has removed everything.
        var send = dialog.GetButton((int)Android.Content.DialogButtonType.Positive);
        input.TextChanged += (_, _) =>
        {
            if (send is not null)
                send.Enabled = !string.IsNullOrWhiteSpace(input.Text);
        };

        return result.Task;
    }

    private static async Task SendAsync(ISpamReporter reporter, string text, bool isSpam)
    {
        var loc = LocalizationManager.Instance;
        string result;
        try
        {
            await Task.Run(() => reporter.ReportAsync(text, isSpam));
            result = loc["Spam_ReportThanks"];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sending the spam report failed: {ex}");
            result = loc["Spam_ReportFailed"];
        }

        MainThread.BeginInvokeOnMainThread(() => Ui.Toast.Show(result));
    }
}
