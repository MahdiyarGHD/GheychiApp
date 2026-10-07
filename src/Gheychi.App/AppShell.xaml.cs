using Gheychi.App.Localization;
using Gheychi.App.Pages;
using Gheychi.App.Platforms.Android.Notifications;

namespace Gheychi.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        FlowDirection = CultureService.GetFlowDirection();

        Loaded += (_, _) =>
        {
            ChatLaunchRequests.Requested += ShowMessagesTab;
            SpamTabRequests.Requested += ShowSpamTab;
            ShowSpamTab();
        };
        Unloaded += (_, _) =>
        {
            ChatLaunchRequests.Requested -= ShowMessagesTab;
            SpamTabRequests.Requested -= ShowSpamTab;
        };
    }

    /// <summary>A tapped spam summary.</summary>
    private void ShowSpamTab()
    {
        if (SpamTabRequests.Take() && CurrentPage is not SpamPage)
            _ = GoToAsync("//spam");
    }

    /// <summary>A tapped notification must land on the inbox, whichever tab was open; the inbox then opens the chat.</summary>
    private void ShowMessagesTab()
    {
        if (CurrentPage is not MessagesPage)
            _ = GoToAsync("//messages");
    }
}
