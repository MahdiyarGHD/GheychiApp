using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Android.Views;
using AndroidX.Fragment.App;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// Keeps every tab's page alive when switching tabs. Shell only keeps the tab being left and the tab being opened, and
/// removes the third. A removed tab drops its page's handler and is given a new MAUI context when it comes back, so
/// every view on the page is created again: going back to the inbox from Settings rebuilt the inbox, its list and all
/// of its overlays, which took seconds. Here a tab switch only hides one tab and shows the other.
/// </summary>
internal sealed class KeepTabsShellItemRenderer : ShellItemRenderer
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // Shell's own record of the tab fragments, so that it still finds and removes them when the Shell is torn down.
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(ShellItemRendererBase))]
    private static readonly FieldInfo? FragmentMapField = typeof(ShellItemRendererBase).GetField("_fragmentMap", Private);

    private static readonly FieldInfo? CurrentFragmentField = typeof(ShellItemRendererBase).GetField("_currentFragment", Private);

    // A fragment whose add is still waiting in an uncommitted transaction is not IsAdded yet; adding it twice throws.
    private readonly HashSet<Fragment> _added = [];

    private static WeakReference<KeepTabsShellItemRenderer>? _instance;

    public KeepTabsShellItemRenderer(IShellContext shellContext) : base(shellContext)
    {
        _instance = new WeakReference<KeepTabsShellItemRenderer>(this);
    }

    /// <summary>
    /// Builds a tab that has not been opened yet, so opening it the first time does not build its page then. Shell
    /// builds a tab only when it is shown: the page, its views and the first layout all ran on the tap.
    /// </summary>
    public static Task PrebuildAsync(string route) =>
        _instance is not null && _instance.TryGetTarget(out var renderer) ? renderer.PrebuildTabAsync(route) : Task.CompletedTask;

    private async Task PrebuildTabAsync(string route)
    {
        if (!IsAdded || View is null
            || FragmentMapField?.GetValue(this) is not Dictionary<Element, IShellObservableFragment> fragments
            || CurrentFragmentField is null
            || ShellItem?.Items.FirstOrDefault(s => s.CurrentItem?.Route == route) is not { } section
            || fragments.ContainsKey(section))
            return;

        var target = GetOrCreateFragmentForTab(section);
        fragments[section] = target;
        _added.Add(target.Fragment);

        // Added visible but not drawn: a hidden (gone) tab is never laid out, and its page is only created by the
        // tab's pager during layout.
        ChildFragmentManager.BeginTransaction().Add(GetNavigationTarget().Id, target.Fragment).CommitNowAllowingStateLoss();
        if (target.Fragment.View is not { } view)
            return;
        view.Visibility = ViewStates.Invisible;

        for (var waited = 0; waited < 3000 && !IsLaidOut(section); waited += 50)
            await Task.Delay(50);
        await Task.Delay(100);

        // Opened while it was being built: it is the visible tab now.
        if (ReferenceEquals(CurrentFragmentField.GetValue(this), target) || !target.Fragment.IsAdded)
            return;

        ChildFragmentManager.BeginTransaction().Hide(target.Fragment).CommitAllowingStateLoss();
    }

    private static bool IsLaidOut(ShellSection section) =>
        section.CurrentItem is IShellContentController { Page.Handler.PlatformView: global::Android.Views.View { Width: > 0 } };

    protected override Task<bool> HandleFragmentUpdate(ShellNavigationSource navSource, ShellSection shellSection, Page page, bool animated)
    {
        // Pushed pages are left to Shell; the app does not push any, and a missing field means a MAUI update changed them.
        if (navSource != ShellNavigationSource.ShellSectionChanged
            || shellSection is null
            || shellSection.Stack.Count != 1
            || FragmentMapField?.GetValue(this) is not Dictionary<Element, IShellObservableFragment> fragments
            || CurrentFragmentField is null)
            return base.HandleFragmentUpdate(navSource, shellSection!, page, animated);

        var first = fragments.Count == 0;
        if (!fragments.TryGetValue(shellSection, out var target))
            fragments[shellSection] = target = GetOrCreateFragmentForTab(shellSection);

        var current = CurrentFragmentField.GetValue(this) as IShellObservableFragment;
        if (ReferenceEquals(target, current))
            return Task.FromResult(true);

        var transaction = ChildFragmentManager.BeginTransaction();
        if (current is not null)
            transaction.Hide(current.Fragment);
        if (!target.Fragment.IsAdded && _added.Add(target.Fragment))
            transaction.Add(GetNavigationTarget().Id, target.Fragment);
        transaction.Show(target.Fragment);
        if (first)
            transaction.SetReorderingAllowed(true);
        transaction.CommitAllowingStateLoss();

        // Opened while PrebuildTabAsync was still building it: the fragment was never hidden, so Show leaves the view
        // as that left it, not drawn.
        if (target.Fragment.View is { Visibility: ViewStates.Invisible } view)
            view.Visibility = ViewStates.Visible;

        CurrentFragmentField.SetValue(this, target);
        return Task.FromResult(true);
    }
}
