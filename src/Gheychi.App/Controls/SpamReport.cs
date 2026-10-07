using Gheychi.App.Localization;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls;

/// <summary>A user's correction of the spam detector: from the Spam tab ("not spam") or a chat ("spam").</summary>
internal static class SpamReport
{
    /// <summary>Sends each body as its own report in the background, then tells the user how it went.</summary>
    public static void Submit(IEnumerable<string> bodies, bool isSpam)
    {
        var texts = bodies.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        if (texts.Count == 0 || IPlatformApplication.Current?.Services.GetService<ISpamReporter>() is not { } reporter)
            return;

        _ = SendAsync(reporter, texts, isSpam);
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
