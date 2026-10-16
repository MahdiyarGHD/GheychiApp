using Avalonia.Interactivity;
using Gheychi.App.Services;

namespace Gheychi.App.Controls.Settings;

public partial class CodesSettingsScreen : SettingsScreen
{
    private bool _binding;

    public CodesSettingsScreen()
    {
        InitializeComponent();
    }

    public override void OnShown()
    {
        _binding = true;
        AutoCopySwitch.IsChecked = AppPreferences.AutoCopyDynamicPassword;
        _binding = false;
    }

    private void OnAutoCopyToggled(object? sender, RoutedEventArgs e)
    {
        if (!_binding)
            AppPreferences.AutoCopyDynamicPassword = AutoCopySwitch.IsChecked == true;
    }
}
