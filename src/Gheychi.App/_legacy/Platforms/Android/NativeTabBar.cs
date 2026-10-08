using Android.Views;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.ApplicationModel;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// The Shell's bottom tab bar, held hidden while a chat opened from a notification is up. Shell.SetTabBarIsVisible
/// does nothing when the value is unchanged or the platform view was not ready, and the Shell's own resume and
/// layout work can put the bar back after it was hidden. While held, every frame is checked before it is drawn and
/// a visible bar is hidden again, so the bar is never on screen next to the chat.
/// </summary>
internal static class NativeTabBar
{
    private static WeakReference<BottomNavigationView>? _cached;
    private static Guard? _guard;

    public static bool IsHeld => _guard is not null;

    /// <summary>Hides the bar now and keeps it hidden until <see cref="Release"/>.</summary>
    public static void Hold()
    {
        try
        {
            var decor = Platform.CurrentActivity?.Window?.DecorView;
            if (_guard is null && decor?.ViewTreeObserver is { IsAlive: true } observer)
            {
                _guard = new Guard(decor);
                observer.AddOnPreDrawListener(_guard);
            }

            Apply(ViewStates.Gone);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tab bar hold failed: {ex}");
        }
    }

    /// <summary>Stops keeping the bar hidden; showing it again is left to the caller.</summary>
    public static void Release()
    {
        var guard = _guard;
        _guard = null;
        if (guard is null)
            return;

        try
        {
            if (guard.Decor.ViewTreeObserver is { IsAlive: true } observer)
                observer.RemoveOnPreDrawListener(guard);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tab bar release failed: {ex}");
        }
    }

    public static void Show()
    {
        if (!IsHeld)
            Apply(ViewStates.Visible);
    }

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

    private sealed class Guard(global::Android.Views.View decor) : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
    {
        public global::Android.Views.View Decor { get; } = decor;

        // Returning false cancels this frame: the hidden bar is laid out first, so the frame that
        // reaches the screen is already the final one.
        public bool OnPreDraw()
        {
            try
            {
                var bar = Locate();
                if (bar is not null && bar.Visibility != ViewStates.Gone)
                {
                    bar.Visibility = ViewStates.Gone;
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tab bar guard failed: {ex}");
            }

            return true;
        }
    }
}
