using Gheychi.App.Localization;
using Gheychi.App.Ui;
using Gheychi.Core.Spam;

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
        var loc = LocalizationManager.Instance;
        return Sheet.PromptAsync(
            loc[isSpam ? "Spam_ReportSpamConfirmTitle" : "Spam_ReportHamConfirmTitle"],
            loc["Spam_ReportConfirmMessage"],
            body,
            null,
            loc["Spam_ReportConfirmSend"],
            loc["Chat_Cancel"],
            multiline: true);
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
