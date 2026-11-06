using System.Security.Cryptography;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Gheychi.App.Platforms.Android.Receivers;

#if SELF_UPDATE
// Only in builds that update themselves: Google Play restricts this permission, and Play updates its builds itself.
[assembly: UsesPermission(global::Android.Manifest.Permission.RequestInstallPackages)]
#endif

namespace Gheychi.App.Platforms.Android.Services;

/// <summary>Hands a downloaded update to the system's package installer, which asks the user to confirm.</summary>
internal static class ApkInstaller
{
    public const string ActionStatus = "com.evergreen.gheychiapp.INSTALL_STATUS";
    private const int StatusRequestCode = 7401;

    public enum ApkCheck
    {
        Ok,
        Unreadable,
        OtherApp,
        OtherSigner
    }

    /// <summary>
    /// The installer's final answer, raised off the main thread. A successful update ends this process, so success
    /// is rarely heard.
    /// </summary>
    public static event Action<PackageInstallStatus>? Finished;

    /// <summary>Whether the user let this app install apps ("Install unknown apps", Android 8 on).</summary>
    public static bool MayInstall(Context context) =>
        !OperatingSystem.IsAndroidVersionAtLeast(26) || context.PackageManager?.CanRequestPackageInstalls() == true;

    /// <summary>The system screen where the user lets this app install apps.</summary>
    public static void OpenInstallPermission(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var intent = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources,
            global::Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    /// <summary>
    /// Whether the APK is this app, signed with a key the installed app is signed with. Asked before the installer is,
    /// which would turn a differently signed APK down only after the user confirmed it, with no reason given.
    /// </summary>
    public static ApkCheck Check(Context context, string apkPath)
    {
        var packages = context.PackageManager!;
#pragma warning disable CA1422 // The flags-int overloads and Signatures are what Android 7 and 8 have.
        var flags = OperatingSystem.IsAndroidVersionAtLeast(28) ? PackageInfoFlags.SigningCertificates : PackageInfoFlags.Signatures;
        var apk = packages.GetPackageArchiveInfo(apkPath, flags);
        if (apk is null)
            return ApkCheck.Unreadable;
        if (apk.PackageName != context.PackageName)
            return ApkCheck.OtherApp;

        var apkSigners = Signers(apk);
        // Before Android 9 an APK signed with the v2 scheme alone shows no signers here; the installer still checks.
        if (apkSigners.Count == 0)
            return ApkCheck.Ok;

        // Any certificate in common: an APK whose key was rotated lists the old one in its history.
        return apkSigners.Overlaps(Signers(packages.GetPackageInfo(context.PackageName!, flags)!)) ? ApkCheck.Ok : ApkCheck.OtherSigner;
    }

    private static HashSet<string> Signers(PackageInfo info)
    {
        var signatures = OperatingSystem.IsAndroidVersionAtLeast(28) && info.SigningInfo is { } signing
            ? signing.HasMultipleSigners ? signing.GetApkContentsSigners() : signing.GetSigningCertificateHistory()
            : info.Signatures?.ToArray();
#pragma warning restore CA1422
        return (signatures ?? []).Select(s => Convert.ToHexString(SHA256.HashData(s.ToByteArray()!))).ToHashSet();
    }

    /// <summary>Copies the APK into an install session and commits it. Call off the main thread.</summary>
    public static void Install(Context context, string apkPath)
    {
        var installer = context.PackageManager!.PackageInstaller;
        var length = new FileInfo(apkPath).Length;
        var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        parameters.SetAppPackageName(context.PackageName);
        parameters.SetSize(length);

        using var session = installer.OpenSession(installer.CreateSession(parameters));
        try
        {
            using (var input = File.OpenRead(apkPath))
            using (var output = session.OpenWrite("update.apk", 0, length))
            {
                input.CopyTo(output);
                session.Fsync(output);
            }

            var intent = new Intent(context, typeof(ApkInstallReceiver));
            intent.SetAction(ActionStatus);
            // Mutable: the installer puts its answer in it.
            var flags = PendingIntentFlags.UpdateCurrent | (OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0);
            var status = PendingIntent.GetBroadcast(context, StatusRequestCode, intent, flags)!;
            session.Commit(status.IntentSender!);
        }
        catch
        {
            session.Abandon();
            throw;
        }
    }

    /// <summary>The installer's answer, from <see cref="ApkInstallReceiver"/>.</summary>
    public static void OnStatus(Context context, Intent intent)
    {
        var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
        if (status != PackageInstallStatus.PendingUserAction)
        {
            System.Diagnostics.Debug.WriteLine($"Update install ended: {status} {intent.GetStringExtra(PackageInstaller.ExtraStatusMessage)}");
            Finished?.Invoke(status);
            return;
        }

#pragma warning disable CA1422
        var confirm = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent
            : intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422
        if (confirm is null)
        {
            Finished?.Invoke(PackageInstallStatus.Failure);
            return;
        }

        // The install was started from the app on screen, so the system lets the prompt open from here.
        confirm.AddFlags(ActivityFlags.NewTask);
        ((Context?)Platform.CurrentActivity ?? context).StartActivity(confirm);
    }
}
