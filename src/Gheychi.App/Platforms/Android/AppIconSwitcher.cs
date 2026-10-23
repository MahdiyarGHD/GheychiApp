using Android.Content;
using Android.Content.PM;
using Gheychi.App.Services;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// The launcher icon. AndroidManifest.xml declares one launcher alias per icon, and the one that is enabled is the
/// icon on the home screen and in the app drawer; switching enables one alias and disables the other.
/// </summary>
internal static class AppIconSwitcher
{
    // The name MainActivity had before the aliases, kept so an update leaves the existing home-screen icon in place.
    private const string GheychiAlias = "crc6426d4b22b25096775.MainActivity";
    private const string StockAlias = "com.evergreen.gheychiapp.LauncherStock";

    /// <summary>
    /// <see cref="AppPreferences.IconGheychi"/> or <see cref="AppPreferences.IconStock"/>, as the phone has it now.
    /// Asks the system, so it is for the Appearance screen and not for start-up.
    /// </summary>
    public static string Current()
    {
        var context = Platform.AppContext;
        var state = context.PackageManager?.GetComponentEnabledSetting(new ComponentName(context, StockAlias));
        return state == ComponentEnabledState.Enabled ? AppPreferences.IconStock : AppPreferences.IconGheychi;
    }

    /// <summary>
    /// Shows the icon with that id. The new alias is enabled before the old one is disabled, so the app is never
    /// without a launcher entry, and DontKillApp keeps the running app alive while the launcher redraws.
    /// </summary>
    public static void Apply(string icon)
    {
        var context = Platform.AppContext;
        if (context.PackageManager is not { } packages)
            return;

        var (on, off) = icon == AppPreferences.IconStock ? (StockAlias, GheychiAlias) : (GheychiAlias, StockAlias);
        packages.SetComponentEnabledSetting(new ComponentName(context, on), ComponentEnabledState.Enabled, ComponentEnableOption.DontKillApp);
        packages.SetComponentEnabledSetting(new ComponentName(context, off), ComponentEnabledState.Disabled, ComponentEnableOption.DontKillApp);
        AppPreferences.AppIcon = icon;
    }
}
