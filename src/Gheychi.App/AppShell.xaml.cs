using Gheychi.App.Localization;

namespace Gheychi.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        FlowDirection = CultureService.GetFlowDirection();
    }
}
