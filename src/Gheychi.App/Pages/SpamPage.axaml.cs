using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Pages;

public partial class SpamPage : UserControl
{
    private bool _loaded;
    private ScrollViewer? _scroll;

    private SpamViewModel? Vm => DataContext as SpamViewModel;

    public SpamPage()
    {
        InitializeComponent();
        Focusable = true;

        if (IPlatformApplication.Current?.Services?.GetService(typeof(SpamViewModel)) is SpamViewModel viewModel)
        {
            DataContext = viewModel;
            viewModel.Items.CollectionChanged += OnItemsChanged;
            _ = LoadAsync(viewModel);
        }

        SpamList.TemplateApplied += (_, e) => _scroll = e.NameScope.Find<ScrollViewer>("SpamScroll");
        Ui.RowPressEffect.Attach(SpamList, SpamHost);
    }

    /// <summary>Asked to open Settings on its spam section (the header's gear); the tab host does the switch.</summary>
    public event Action? OpenSpamSettingsRequested;

    /// <summary>The tab was selected: the spam summary notification has nothing left to tell.</summary>
    public void OnShown()
    {
        var context = Platform.AppContext;
        _ = Task.Run(() => SpamDigestNotifier.MarkSeen(context));
    }

    public void OnHidden()
    {
        if (SearchBar.IsVisible)
            ClearFocus();
    }

    /// <summary>The Spam tab was tapped while shown: a menu or the search closes, otherwise the list goes to its top.</summary>
    public void OnTabReselected()
    {
        if (!HandleBack())
            Ui.ScrollAnimator.ToTop(_scroll);
    }

    /// <summary>True when it consumed the back press.</summary>
    public bool HandleBack()
    {
        if (Overlay.HandleBack())
            return true;

        if (!SearchBar.IsVisible)
            return false;

        CloseSearch();
        return true;
    }

    private async Task LoadAsync(SpamViewModel viewModel)
    {
        try
        {
            await viewModel.EnsureLoadedAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loading spam failed: {ex}");
        }

        _loaded = true;
        UpdateEmptyState();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    private void UpdateEmptyState() => EmptyState.IsVisible = _loaded && Vm is { Items.Count: 0 };

    // No focus-clear API: the page takes focus, which also drops the keyboard.
    private void ClearFocus()
    {
        if (SearchEntry.IsFocused)
            Focus();
    }

    private void OnSettingsTapped(object? sender, TappedEventArgs e) => OpenSpamSettingsRequested?.Invoke();

    private void OnSearchTapped(object? sender, TappedEventArgs e)
    {
        AppHeader.IsVisible = false;
        SearchBar.IsVisible = true;
        Dispatcher.UIThread.Post(() => SearchEntry.Focus(), DispatcherPriority.Loaded);
    }

    private void OnSearchCloseTapped(object? sender, TappedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        ClearFocus();
        if (Vm is not null)
            Vm.SearchText = string.Empty;
        SearchBar.IsVisible = false;
        AppHeader.IsVisible = true;
    }

    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is SpamItem item)
        {
            ClearFocus();
            Overlay.ShowMenu(item);
        }
    }

    private async void OnClearAllTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            if (Vm is not null && await Overlay.ConfirmAsync(loc["Spam_ClearAllTitle"], loc["Spam_ClearAllMessage"], loc["Spam_ClearAll"]))
                await Vm.ClearAllAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clear all failed: {ex}");
        }
    }
}
