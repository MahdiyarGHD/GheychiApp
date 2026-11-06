using Android.Content;
using Gheychi.App.Platforms.Android.Services;

namespace Gheychi.App.Platforms.Android.Receivers;

/// <summary>The package installer's answers about an update. Not exported: only the session's own intent reaches it.</summary>
[BroadcastReceiver(Name = "com.evergreen.gheychiapp.ApkInstallReceiver", Exported = false)]
public class ApkInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null && intent?.Action == ApkInstaller.ActionStatus)
            ApkInstaller.OnStatus(context, intent);
    }
}
