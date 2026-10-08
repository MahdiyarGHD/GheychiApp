using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.Ui;
using Gheychi.Core.Spam;
using Gheychi.Core.Updates;

namespace Gheychi.App.Controls.Settings;

public partial class AboutSettingsScreen : SettingsScreen
{
    private readonly ISpamModelUpdater? _models;
    private readonly AppUpdates? _appUpdates;
    private bool _checking;

    public AboutSettingsScreen()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        _models = services?.GetService<ISpamModelUpdater>();
        _appUpdates = services?.GetService<AppUpdates>();
        if (_appUpdates is not null)
            _appUpdates.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(ShowAppUpdate);
        VersionValue.Text = SettingsUi.Digits($"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})");
    }

    public override void OnShown()
    {
        ShowAppUpdate();
        _ = ShowModelVersionAsync();
    }

    private void ShowAppUpdate()
    {
        if (_checking)
            return;

        var loc = LocalizationManager.Instance;
        var available = _appUpdates?.GetAvailable();
        AppUpdateTitle.Text = loc[available is null ? "Settings_AppUpdate" : "Settings_AppUpdateAvailable"];
        AppUpdateHint.Text = available is null
            ? loc["Settings_AppUpdateHint"]
            : string.Format(loc["Settings_AppUpdateAvailableHint"], SettingsUi.Digits(available.Tag.TrimStart('v', 'V')));
    }

    private async void OnAppUpdateTapped(object? sender, TappedEventArgs e)
    {
        if (_appUpdates is not { } updates || _checking)
            return;

        var loc = LocalizationManager.Instance;
        try
        {
            if (updates.GetAvailable() is { } release)
            {
                // An update found before direct links were stored has only its page.
                await Launcher.Default.OpenAsync(new Uri(release.DownloadUrl ?? release.PageUrl));
                return;
            }

            _checking = true;
            AppUpdateHint.Text = loc["Settings_ModelChecking"];
            if (await Task.Run(() => updates.CheckAsync(force: true)) is null)
                Toast.Show(loc["Settings_AppUpToDate"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App update check failed: {ex}");
            Toast.Show(loc["Settings_ModelCheckFailed"]);
        }
        finally
        {
            _checking = false;
            ShowAppUpdate();
        }
    }

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
