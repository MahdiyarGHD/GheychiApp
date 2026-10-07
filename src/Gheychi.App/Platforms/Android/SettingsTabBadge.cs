using Google.Android.Material.BottomNavigation;
using Gheychi.Core.Spam;
using Gheychi.Core.Updates;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// A dot on the Settings tab while a newer app version or spam model is waiting. The update itself is only offered
/// inside Settings, which nobody opens to look for one.
/// </summary>
internal static class SettingsTabBadge
{
    private const string SettingsRoute = "settings";

    private static WeakReference<BottomNavigationView>? _bar;
    private static bool _subscribed;
    private static bool _shown;

    /// <summary>Called whenever Shell styles its tab bar, which can follow a rebuild of the bar's items.</summary>
    public static void Attach(BottomNavigationView bar)
    {
        // Shell styles the bar again on tab switches; the state is only read again for a new bar.
        if (_bar is not null && _bar.TryGetTarget(out var known) && ReferenceEquals(known, bar))
        {
            Apply();
            return;
        }

        _bar = new WeakReference<BottomNavigationView>(bar);
        Subscribe();
        _ = UpdateAsync();
    }

    private static void Subscribe()
    {
        if (_subscribed || IPlatformApplication.Current?.Services is not { } services)
            return;

        _subscribed = true;
        if (services.GetService<AppUpdates>() is { } app)
            app.Changed += (_, _) => _ = UpdateAsync();
        if (services.GetService<SpamModelUpdates>() is { } model)
            model.Changed += (_, _) => _ = UpdateAsync();
    }

    private static async Task UpdateAsync()
    {
        try
        {
            var services = IPlatformApplication.Current?.Services;
            var app = services?.GetService<AppUpdates>()?.GetAvailable() is not null;
            var model = !app && services?.GetService<SpamModelUpdates>() is { } models
                && await Task.Run(() => models.GetAvailableAsync()) is not null;

            _shown = app || model;
            MainThread.BeginInvokeOnMainThread(Apply);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings tab badge failed: {ex}");
        }
    }

    private static void Apply()
    {
        try
        {
            if (_bar is null || !_bar.TryGetTarget(out var bar) || SettingsTabIndex() is not { } index)
                return;

            // Shell numbers its tab bar items by position.
            if (_shown)
                bar.GetOrCreateBadge(index).SetVisible(true);
            else
                bar.GetBadge(index)?.SetVisible(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings tab badge failed: {ex}");
        }
    }

    private static int? SettingsTabIndex()
    {
        var tabs = Shell.Current?.CurrentItem?.Items;
        if (tabs is null)
            return null;

        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].CurrentItem?.Route == SettingsRoute)
                return i;
        }

        return null;
    }
}
