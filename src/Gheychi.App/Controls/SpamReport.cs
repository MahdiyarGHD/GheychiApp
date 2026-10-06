using Gheychi.App.Localization;

namespace Gheychi.App.Controls;

/// <summary>A user's correction of the spam detector: from the Spam tab ("not spam") or a chat ("spam").</summary>
internal static class SpamReport
{
    // TODO: keep each report (text + verdict) so it can feed the next model; for now it only thanks the user.
    public static void Submit(IEnumerable<string> bodies, bool isSpam) =>
        Toast.Show(LocalizationManager.Instance["Spam_ReportThanks"]);
}
