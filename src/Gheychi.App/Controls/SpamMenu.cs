using Gheychi.App.Localization;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls;

/// <summary>The ••• menu of a spam row, the same on the Spam tab and on a profile.</summary>
internal static class SpamMenu
{
    public static async Task ShowAsync(SpamItem item, SpamViewModel viewModel)
    {
        var page = Shell.Current;
        if (page is null)
            return;

        var loc = LocalizationManager.Instance;
        var restore = loc["Spam_ActionRestore"];
        var restoreTrust = loc["Spam_ActionRestoreTrust"];
        var info = loc["Spam_ActionInfo"];
        var delete = loc["Spam_ActionDelete"];

        var choice = await page.DisplayActionSheetAsync(item.Sender, loc["Chat_Cancel"], delete, restore, restoreTrust, info);

        if (choice == restore || choice == restoreTrust)
        {
            if (!await viewModel.RestoreAsync(item, trustSender: choice == restoreTrust))
                await page.DisplayAlertAsync(string.Empty, loc["Spam_RestoreFailed"], loc["Spam_Ok"]);
        }
        else if (choice == info)
        {
            await page.DisplayAlertAsync(loc["Spam_InfoTitle"], viewModel.DescribeInfo(item), loc["Spam_Ok"]);
        }
        else if (choice == delete)
        {
            await viewModel.DeleteAsync(item);
        }
    }

    public static async Task ConfirmClearAllAsync(SpamViewModel viewModel)
    {
        var page = Shell.Current;
        if (page is null)
            return;

        var loc = LocalizationManager.Instance;
        if (await page.DisplayAlertAsync(loc["Spam_ClearAllTitle"], loc["Spam_ClearAllMessage"], loc["Spam_ClearAll"], loc["Chat_Cancel"]))
            await viewModel.ClearAllAsync();
    }
}
