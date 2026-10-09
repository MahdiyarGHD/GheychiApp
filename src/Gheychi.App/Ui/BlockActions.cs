using Gheychi.App.Localization;
using Gheychi.Core.Services;

namespace Gheychi.App.Ui;

/// <summary>Blocking and unblocking a sender from the places that offer it (the profile and the chat's menu).</summary>
internal static class BlockActions
{
    /// <summary>Unblocks at once; blocks after the user confirms. False when nothing changed.</summary>
    public static async Task<bool> ToggleAsync(IBlockedSenders blocked, string address, string name)
    {
        var loc = LocalizationManager.Instance;
        if (blocked.IsBlocked(address))
        {
            blocked.SetBlocked(address, false);
            Toast.Show(loc["Profile_Unblocked"]);
            return true;
        }

        var title = string.Format(loc["Profile_BlockConfirmTitle"], name);
        if (!await Dialogs.AlertAsync(title, loc["Profile_BlockConfirmMessage"], loc["Profile_BlockConfirm"], loc["Chat_Cancel"]))
            return false;

        blocked.SetBlocked(address, true);
        Toast.Show(loc["Profile_Blocked"]);
        return true;
    }
}
