using Android.Content.PM;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.Ui;
using Gheychi.Core.Updates;
using Gheychi.Infrastructure.Updates;

namespace Gheychi.App.Services;

/// <summary>
/// Takes an update from its download to the system's install prompt. Kept out of the About screen so a download
/// carries on, and still shows, when the screen is built again.
/// </summary>
internal static class AppUpdateFlow
{
    private static CancellationTokenSource? _download;
    private static bool _installWhenAllowed;

    static AppUpdateFlow()
    {
        ApkInstaller.Finished += status => MainThread.BeginInvokeOnMainThread(() => OnInstallFinished(status));
        MainActivity.Resumed += OnAppResumed;
    }

    /// <summary>Raised on the main thread when <see cref="Progress"/> or <see cref="Installing"/> changes.</summary>
    public static event Action? Changed;

    /// <summary>The running download's progress from 0 to 1; null while none runs.</summary>
    public static double? Progress { get; private set; }

    /// <summary>The APK is with the system installer, which has not answered yet.</summary>
    public static bool Installing { get; private set; }

    private static ApkDownloader? Downloader => IPlatformApplication.Current?.Services.GetService<ApkDownloader>();

    public static bool IsDownloaded(AppRelease release) => Downloader?.GetDownloaded(release) is not null;

    /// <summary>
    /// The update row was tapped: stops a running download, or downloads the update and then installs it.
    /// Call on the main thread.
    /// </summary>
    public static async Task RunAsync(AppRelease release)
    {
        if (Downloader is not { } downloader)
            return;
        if (_download is { } running)
        {
            running.Cancel();
            return;
        }

        if (downloader.GetDownloaded(release) is { } ready)
        {
            await InstallAsync(release, ready);
            return;
        }

        var loc = LocalizationManager.Instance;
        var cancel = _download = new CancellationTokenSource();
        string path;
        try
        {
            SetProgress(0);
            // Made here, on the main thread, so its reports come back to it.
            var progress = new Progress<double>(value => SetProgress(value));
            path = await Task.Run(() => downloader.DownloadAsync(release, progress, cancel.Token));
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (InvalidDataException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update download rejected: {ex}");
            Toast.Show(loc["AppUpdate_Damaged"]);
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update download failed: {ex}");
            Toast.Show(loc["AppUpdate_DownloadStopped"]);
            return;
        }
        finally
        {
            _download = null;
            cancel.Dispose();
            SetProgress(null);
        }

        // Done while the user was in another app: a prompt out of nowhere. The row offers the install instead.
        if (ChatPresence.IsAppVisible)
            await InstallAsync(release, path);
    }

    private static async Task InstallAsync(AppRelease release, string path)
    {
        var loc = LocalizationManager.Instance;
        var context = Platform.AppContext;
        try
        {
            if (!ApkInstaller.MayInstall(context))
            {
                _installWhenAllowed = true;
                Toast.Show(loc["AppUpdate_AllowInstall"]);
                ApkInstaller.OpenInstallPermission(context);
                return;
            }

            switch (await Task.Run(() => ApkInstaller.Check(context, path)))
            {
                case ApkInstaller.ApkCheck.Unreadable:
                    Downloader?.Clear();
                    Toast.Show(loc["AppUpdate_Damaged"]);
                    return;
                case ApkInstaller.ApkCheck.OtherApp:
                case ApkInstaller.ApkCheck.OtherSigner:
                    ShowSignerMismatch(release);
                    return;
            }

            SetInstalling(true);
            await Task.Run(() => ApkInstaller.Install(context, path));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Starting the update install failed: {ex}");
            SetInstalling(false);
            Toast.Show(loc["AppUpdate_Failed"]);
        }
    }

    // Back from "Install unknown apps": carry on if the user allowed it.
    private static void OnAppResumed()
    {
        if (!_installWhenAllowed)
            return;

        _installWhenAllowed = false;
        var context = Platform.AppContext;
        if (ApkInstaller.MayInstall(context)
            && IPlatformApplication.Current?.Services.GetService<AppUpdates>()?.GetAvailable() is { } release
            && Downloader?.GetDownloaded(release) is { } path)
            _ = InstallAsync(release, path);
    }

    private static void OnInstallFinished(PackageInstallStatus status)
    {
        SetInstalling(false);
        var loc = LocalizationManager.Instance;
        switch (status)
        {
            // Success ends this process; Aborted is the user saying no to the prompt.
            case PackageInstallStatus.Success:
            case PackageInstallStatus.FailureAborted:
                return;
            case PackageInstallStatus.FailureStorage:
                Toast.Show(loc["AppUpdate_NoStorage"]);
                return;
            case PackageInstallStatus.FailureIncompatible:
            case PackageInstallStatus.FailureConflict:
                ShowSignerMismatch(IPlatformApplication.Current?.Services.GetService<AppUpdates>()?.GetAvailable());
                return;
            default:
                Toast.Show(loc["AppUpdate_Failed"]);
                return;
        }
    }

    // Only a reinstall from the release page helps, which costs the app's own data: the user decides there.
    private static void ShowSignerMismatch(AppRelease? release)
    {
        Toast.Show(LocalizationManager.Instance["AppUpdate_SignerMismatch"]);
        if (release is not null)
            _ = Launcher.Default.OpenAsync(new Uri(release.PageUrl));
    }

    private static void SetProgress(double? value)
    {
        Progress = value;
        Changed?.Invoke();
    }

    private static void SetInstalling(bool value)
    {
        Installing = value;
        Changed?.Invoke();
    }
}
