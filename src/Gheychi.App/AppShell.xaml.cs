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

        Loaded += (_, _) => ChatLaunchRequests.Requested += ShowMessagesTab;
        Unloaded += (_, _) => ChatLaunchRequests.Requested -= ShowMessagesTab;
    }

    /// <summary>A tapped notification must land on the inbox, whichever tab was open; the inbox then opens the chat.</summary>
    private void ShowMessagesTab()
    {
        if (CurrentPage is not MessagesPage)
            _ = GoToAsync("//messages");
    }
}
