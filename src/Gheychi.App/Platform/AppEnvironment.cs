using Android.Content;
using Android.Content.PM;
using Android.Provider;
using Uri = Android.Net.Uri;

namespace Gheychi.App;

public static class FileSystem
{
    public static string AppDataDirectory => Platform.AppContext.FilesDir!.AbsolutePath;

    public static string CacheDirectory => Platform.AppContext.CacheDir!.AbsolutePath;

    /// <summary>A file bundled in the APK's assets.</summary>
    public static Task<Stream> OpenAppPackageFileAsync(string name) =>
        Task.FromResult(Platform.AppContext.Assets!.Open(name));
}

public sealed class AppInfo
{
    public static AppInfo Current { get; } = new();

    private AppInfo()
    {
    }

    public string PackageName => Platform.AppContext.PackageName!;

    public string VersionString => Package?.VersionName ?? string.Empty;

    public string BuildString => Package is { } info
        ? (OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode).ToString()
        : string.Empty;

    private PackageInfo? Package => Platform.AppContext.PackageManager!.GetPackageInfo(PackageName, 0);

    /// <summary>The system's page for this app (permissions, notifications).</summary>
    public void ShowSettingsUI()
    {
        var intent = new Intent(Settings.ActionApplicationDetailsSettings)
            .SetData(Uri.FromParts("package", PackageName, null))!
            .AddFlags(ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(intent);
    }
}

public sealed class Launcher
{
    public static Launcher Default { get; } = new();

    private Launcher()
    {
    }

    public Task<bool> OpenAsync(string uri) => OpenAsync(new System.Uri(uri));

    public Task<bool> OpenAsync(System.Uri uri)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, Uri.Parse(uri.OriginalString)).AddFlags(ActivityFlags.NewTask);
            // No ResolveActivity check: from Android 11 it returns null for every app without a <queries> entry.
            Platform.AppContext.StartActivity(intent);
            return Task.FromResult(true);
        }
        catch (ActivityNotFoundException)
        {
            return Task.FromResult(false);
        }
    }
}

public sealed class Clipboard
{
    public static Clipboard Default { get; } = new();

    private Clipboard()
    {
    }

    public Task SetTextAsync(string? text)
    {
        var manager = (ClipboardManager)Platform.AppContext.GetSystemService(Context.ClipboardService)!;
        manager.PrimaryClip = ClipData.NewPlainText("text", text ?? string.Empty);
        return Task.CompletedTask;
    }
}
