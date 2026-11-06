using Avalonia.Input;
using Avalonia.Interactivity;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.Services;
using Gheychi.App.Ui;
using Gheychi.Core.Spam;
using Gheychi.Core.Updates;

namespace Gheychi.App.Controls.Settings;

public partial class AboutSettingsScreen : SettingsScreen
{
    private readonly ISpamModelUpdater? _models;
    private readonly AppUpdates? _appUpdates;
    private bool _checking;
    private bool _binding;

    public AboutSettingsScreen()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        _models = services?.GetService<ISpamModelUpdater>();
        _appUpdates = services?.GetService<AppUpdates>();
        if (_appUpdates is not null)
            _appUpdates.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(ShowAppUpdate);
        AppUpdateFlow.Changed += ShowAppUpdate;
        // A build without the updater (Google Play) has no update rows.
        AppUpdateRows.IsVisible = _appUpdates is not null;
        VersionValue.Text = SettingsUi.Digits($"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})");
    }

    public override void OnShown()
    {
        _binding = true;
        AutoUpdateSwitch.IsChecked = AppPreferences.AutoUpdateCheck;
        _binding = false;
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
        if (available is null)
        {
            AppUpdateHint.Text = loc["Settings_AppUpdateHint"];
            return;
        }

        var version = SettingsUi.Digits(available.Tag.TrimStart('v', 'V'));
        AppUpdateHint.Text = AppUpdateFlow.Progress is { } progress
            ? string.Format(loc["Settings_AppUpdateDownloading"], SettingsUi.Number((int)(progress * 100)))
            : AppUpdateFlow.Installing
                ? loc["Settings_AppUpdateInstalling"]
                : AppUpdateFlow.IsDownloaded(available)
                    ? string.Format(loc["Settings_AppUpdateReady"], version)
                    : available.Size is { } size
                        ? string.Format(loc["Settings_AppUpdateAvailableSizeHint"], version,
                            SettingsUi.Number(Math.Max(1, (int)Math.Round(size / 1048576.0))))
                        : string.Format(loc["Settings_AppUpdateAvailableHint"], version);
    }

    private void OnAutoUpdateToggled(object? sender, RoutedEventArgs e)
    {
        if (_binding)
            return;

        AppPreferences.AutoUpdateCheck = AutoUpdateSwitch.IsChecked == true;
        _ = Task.Run(() => UpdateCheckJob.Schedule(Platform.AppContext));
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
                if (release.DownloadUrl is null)
                    await Launcher.Default.OpenAsync(new Uri(release.PageUrl));
                else
                    await AppUpdateFlow.RunAsync(release);
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
