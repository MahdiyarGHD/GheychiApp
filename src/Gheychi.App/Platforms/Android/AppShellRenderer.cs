using System.Runtime.CompilerServices;
using Android.Widget;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using AView = Android.Views.View;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// Shell has no font setting for its tab titles, which were drawn in the system font; this gives them the app's.
/// Tabs also stay built across tab switches (<see cref="KeepTabsShellItemRenderer"/>).
/// </summary>
internal sealed class AppShellRenderer : ShellRenderer
{
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem) => new KeepTabsShellItemRenderer(this);

    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem) =>
        new TabBarAppearanceTracker(this, shellItem);

    private sealed class TabBarAppearanceTracker(IShellContext shellContext, ShellItem shellItem)
        : ShellBottomNavViewAppearanceTracker(shellContext, shellItem)
    {
        public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
        {
            base.SetAppearance(bottomView, appearance);
            TabBarFont.Attach(bottomView);
            SettingsTabBadge.Attach(bottomView);
        }

        public override void ResetAppearance(BottomNavigationView bottomView)
        {
            base.ResetAppearance(bottomView);
            TabBarFont.Attach(bottomView);
            SettingsTabBadge.Attach(bottomView);
        }
    }
}

/// <summary>The app's own font (Vazirmatn in Persian), for native views MAUI does not style.</summary>
internal static class AppTypeface
{
    private static global::Android.Graphics.Typeface? _typeface;

    public static global::Android.Graphics.Typeface? Get()
    {
        if (_typeface is not null)
            return _typeface;

        if (Application.Current?.Resources.TryGetValue("AppFontFamily", out var family) != true || family is not string name
            || IPlatformApplication.Current?.Services.GetService<IFontManager>() is not { } fonts)
            return null;

        return _typeface = fonts.GetTypeface(Microsoft.Maui.Font.OfSize(name, 0));
    }
}

internal static class TabBarFont
{
    private static readonly ConditionalWeakTable<BottomNavigationView, Relayout> Attached = new();

    // The tab views are rebuilt whenever the Shell rebuilds its menu, so the font is applied again after every layout
    // of the bar; setting the typeface a label already has does nothing.
    public static void Attach(BottomNavigationView bar)
    {
        if (!Attached.TryGetValue(bar, out _))
        {
            var listener = new Relayout();
            Attached.Add(bar, listener);
            bar.AddOnLayoutChangeListener(listener);
        }

        Apply(bar);
    }

    private static void Apply(AView view)
    {
        try
        {
            if (AppTypeface.Get() is not { } typeface)
                return;

            ApplyTo(view, typeface);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tab bar font failed: {ex}");
        }
    }

    private static void ApplyTo(AView view, global::Android.Graphics.Typeface typeface)
    {
        if (view is TextView label)
        {
            if (label.Typeface != typeface)
                label.Typeface = typeface;
            return;
        }

        if (view is global::Android.Views.ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                if (group.GetChildAt(i) is { } child)
                    ApplyTo(child, typeface);
            }
        }
    }

    private sealed class Relayout : Java.Lang.Object, AView.IOnLayoutChangeListener
    {
        public void OnLayoutChange(AView? v, int left, int top, int right, int bottom,
            int oldLeft, int oldTop, int oldRight, int oldBottom)
        {
            if (v is not null)
                Apply(v);
        }
    }
}
