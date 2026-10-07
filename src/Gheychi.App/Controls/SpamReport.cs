using Gheychi.App.Localization;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls;

/// <summary>A user's correction of the spam detector: from the Spam tab ("not spam") or a chat ("spam").</summary>
internal static class SpamReport
{
    /// <summary>
    /// Asks before anything leaves the phone, then sends each body as its own report in the background and tells the
    /// user how it went. False when the user cancelled.
    /// </summary>
    public static async Task<bool> SubmitAsync(IEnumerable<string> bodies, bool isSpam)
    {
        var texts = bodies.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        if (texts.Count == 0 || IPlatformApplication.Current?.Services.GetService<ISpamReporter>() is not { } reporter)
            return false;

        var loc = LocalizationManager.Instance;
        var title = texts.Count == 1
            ? loc[isSpam ? "Spam_ReportSpamConfirmTitle" : "Spam_ReportHamConfirmTitle"]
            : string.Format(loc[isSpam ? "Spam_ReportSpamConfirmTitleMany" : "Spam_ReportHamConfirmTitleMany"], texts.Count);
        try
        {
            // Called from async void tap handlers: nothing here may throw.
            if (Shell.Current is not { } shell
                || !await shell.DisplayAlertAsync(title, loc["Spam_ReportConfirmMessage"], loc["Spam_ReportConfirmSend"], loc["Chat_Cancel"]))
                return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Asking to send the spam report failed: {ex}");
            return false;
        }

        _ = SendAsync(reporter, texts, isSpam);
        return true;
    }

    private static async Task SendAsync(ISpamReporter reporter, List<string> texts, bool isSpam)
    {
        var loc = LocalizationManager.Instance;
        string result;
        try
        {
            await Task.Run(async () =>
            {
                foreach (var text in texts)
                    await reporter.ReportAsync(text, isSpam);
            });
            result = loc["Spam_ReportThanks"];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sending the spam report failed: {ex}");
            result = loc["Spam_ReportFailed"];
        }

        MainThread.BeginInvokeOnMainThread(() => Toast.Show(result));
    }
}
