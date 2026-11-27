using Gheychi.App.Localization;

namespace Gheychi.App.Ui;

/// <summary>Links to the project's GitHub pages (issues, privacy policy).</summary>
internal static class HelpLink
{
    private const string Repository = "MahdiyarGHD/GheychiApp";
    public static readonly string IssuesUrl = $"https://github.com/{Repository}/issues";
    public static readonly string PrivacyPolicyUrl = $"https://github.com/{Repository}/blob/main/PRIVACY.md";

    public static Task OpenAsync() => OpenUrlAsync(IssuesUrl, "issues page");

    public static Task OpenPrivacyPolicyAsync() => OpenUrlAsync(PrivacyPolicyUrl, "privacy policy");

    private static async Task OpenUrlAsync(string url, string label)
    {
        try
        {
            if (!await Launcher.Default.OpenAsync(url))
                await Dialogs.AlertAsync(string.Empty, LocalizationManager.Instance["Chat_LinkOpenFailed"], "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening the {label} failed: {ex}");
        }
    }
}
