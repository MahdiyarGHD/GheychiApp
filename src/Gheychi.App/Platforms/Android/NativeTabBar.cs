using Android.Views;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.ApplicationModel;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// The Shell's bottom tab bar, hidden and shown directly. Shell.SetTabBarIsVisible does nothing when the
/// value is unchanged or the platform view was not ready, which after an app resume leaves the bar on screen
/// until a later layout pass; a chat opened from a notification has to be free of it from the first frame.
/// </summary>
internal static class NativeTabBar
{
    private static WeakReference<BottomNavigationView>? _cached;

    public static void Hide() => Apply(ViewStates.Gone);

    public static void Show() => Apply(ViewStates.Visible);

    private static void Apply(ViewStates state)
    {
        try
        {
            var bar = Locate();
            if (bar is not null && bar.Visibility != state)
                bar.Visibility = state;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tab bar update failed: {ex}");
        }
    }

    // The view tree holds every inbox row and built overlay, so it is searched once and the bar remembered.
    private static BottomNavigationView? Locate()
    {
        if (_cached is not null && _cached.TryGetTarget(out var cached) && cached.IsAttachedToWindow)
            return cached;

        var found = Find(Platform.CurrentActivity?.Window?.DecorView);
        _cached = found is null ? null : new WeakReference<BottomNavigationView>(found);
        return found;
    }

    // Last child first: the bar sits after the page content in the Shell's layout.
    private static BottomNavigationView? Find(global::Android.Views.View? view)
    {
        if (view is BottomNavigationView bar)
            return bar;

        if (view is ViewGroup group)
        {
            for (var i = group.ChildCount - 1; i >= 0; i--)
            {
                if (Find(group.GetChildAt(i)) is { } found)
                    return found;
            }
        }

        return null;
    }
}
