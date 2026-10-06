using Gheychi.App.Localization;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls.Settings;

public partial class AboutSettingsScreen : SettingsScreen
{
    private readonly ISpamModelUpdater? _models;

    public AboutSettingsScreen()
    {
        InitializeComponent();
        MirrorChevrons();
        _models = IPlatformApplication.Current?.Services.GetService<ISpamModelUpdater>();
        VersionValue.Text = SettingsUi.Digits($"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})");
    }

    public override void OnShown() => _ = ShowModelVersionAsync();

    private async Task ShowModelVersionAsync()
    {
        var loc = LocalizationManager.Instance;
        try
        {
            ModelValue.Text = _models is not null && await _models.GetActiveVersionAsync() is { } version
                ? string.Format(loc["Spam_InfoModelValue"], SettingsUi.Number(version))
                : loc["Settings_ModelNone"];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the spam model version failed: {ex}");
        }
    }

    private void OnLicensesTapped(object? sender, TappedEventArgs e) => RequestOpen(SettingsScreenKind.Licenses);
}
