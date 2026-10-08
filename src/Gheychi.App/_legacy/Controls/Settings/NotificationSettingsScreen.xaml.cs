using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Services;

namespace Gheychi.App.Controls.Settings;

public partial class NotificationSettingsScreen : SettingsScreen
{
    private bool _binding;

    public NotificationSettingsScreen()
    {
        InitializeComponent();
        MirrorChevrons();
    }

    public override void OnShown()
    {
        _binding = true;
        LockScreenSwitch.IsToggled = AppPreferences.ShowContentOnLockScreen;
        _binding = false;
    }

    private void OnLockScreenToggled(object? sender, ToggledEventArgs e)
    {
        if (!_binding)
            AppPreferences.ShowContentOnLockScreen = e.Value;
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
