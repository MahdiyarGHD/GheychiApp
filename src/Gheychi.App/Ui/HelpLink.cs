using Gheychi.App.Localization;
using Gheychi.Infrastructure.Updates;

namespace Gheychi.App.Ui;

/// <summary>"Help &amp; feedback": the issues of the project on GitHub, where a problem is reported or an idea suggested.</summary>
internal static class HelpLink
{
    public static readonly string IssuesUrl = $"https://github.com/{GitHubAppReleaseSource.Repository}/issues";

    public static async Task OpenAsync()
    {
        try
        {
            if (!await Launcher.Default.OpenAsync(IssuesUrl))
                await Dialogs.AlertAsync(string.Empty, LocalizationManager.Instance["Chat_LinkOpenFailed"], "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening the issues page failed: {ex}");
        }
    }
}
