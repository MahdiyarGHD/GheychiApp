using Avalonia.Input;
using Avalonia.Interactivity;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Services;

namespace Gheychi.App.Controls.Settings;

public partial class NotificationSettingsScreen : SettingsScreen
{
    private bool _binding;

    public NotificationSettingsScreen()
    {
        InitializeComponent();
    }

    public override void OnShown()
    {
        _binding = true;
        LockScreenSwitch.IsChecked = AppPreferences.ShowContentOnLockScreen;
        _binding = false;
    }

    private void OnLockScreenToggled(object? sender, RoutedEventArgs e)
    {
        if (!_binding)
            AppPreferences.ShowContentOnLockScreen = LockScreenSwitch.IsChecked == true;
    }

    private void OnSystemSettingsTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            NotificationChannels.OpenAppSettings(Platform.AppContext);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening notification settings failed: {ex}");
        }
    }
}
