using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using AndroidX.Fragment.App;
using Microsoft.Maui.Controls.Platform.Compatibility;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// Keeps every tab's page alive when switching tabs. Shell only keeps the tab being left and the tab being opened, and
/// removes the third. A removed tab drops its page's handler and is given a new MAUI context when it comes back, so
/// every view on the page is created again: going back to the inbox from Settings rebuilt the inbox, its list and all
/// of its overlays, which took seconds. Here a tab switch only hides one tab and shows the other.
/// </summary>
internal sealed class KeepTabsShellItemRenderer(IShellContext shellContext) : ShellItemRenderer(shellContext)
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // Shell's own record of the tab fragments, so that it still finds and removes them when the Shell is torn down.
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(ShellItemRendererBase))]
    private static readonly FieldInfo? FragmentMapField = typeof(ShellItemRendererBase).GetField("_fragmentMap", Private);

    private static readonly FieldInfo? CurrentFragmentField = typeof(ShellItemRendererBase).GetField("_currentFragment", Private);

    // A fragment whose add is still waiting in an uncommitted transaction is not IsAdded yet; adding it twice throws.
    private readonly HashSet<Fragment> _added = [];

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

        CurrentFragmentField.SetValue(this, target);
        return Task.FromResult(true);
    }
}
