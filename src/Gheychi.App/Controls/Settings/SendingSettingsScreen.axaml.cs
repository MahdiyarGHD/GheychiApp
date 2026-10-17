using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.Services;
using Gheychi.App.Ui;
using Gheychi.Core.Scheduling;

namespace Gheychi.App.Controls.Settings;

public partial class SendingSettingsScreen : SettingsScreen
{
    public SendingSettingsScreen()
    {
        InitializeComponent();
        ExactRow.IsVisible = OperatingSystem.IsAndroidVersionAtLeast(31);
    }

    public override void OnShown()
    {
        ShowDelay();
        ShowExact();
    }

    private static string DelayLabel(int seconds) => seconds == 0
        ? LocalizationManager.Instance["Settings_SendDelayOff"]
        : string.Format(LocalizationManager.Instance["Settings_SendDelayValue"], seconds);

    private void ShowDelay() => SendDelayValue.Text = DelayLabel(AppPreferences.SendDelaySeconds);

    private void ShowExact()
    {
        var allowed = new ScheduledSendAlarm().CanFireExactly;
        ExactHint.Text = LocalizationManager.Instance[allowed ? "Settings_ExactAlarmsOn" : "Settings_ExactAlarmsOff"];
    }

    private async void OnSendDelayTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            var options = SendSchedule.DelayOptions;
            var labels = options.Select(DelayLabel).ToArray();
            var choice = await Dialogs.ActionSheetAsync(loc["Settings_SendDelay"], loc["Chat_Cancel"], null, labels);
            var index = Array.IndexOf(labels, choice);
            if (index < 0)
                return;

            AppPreferences.SendDelaySeconds = options[index];
            ShowDelay();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Choosing the send delay failed: {ex}");
        }
    }

    private void OnExactAlarmsTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            ScheduledSendAlarm.OpenExactAlarmSettings(Platform.AppContext);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening the alarm settings failed: {ex}");
        }
    }
}
