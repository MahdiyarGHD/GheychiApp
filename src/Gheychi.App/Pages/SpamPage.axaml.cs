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
    private readonly SpamScanViewModel? _scan;
    private bool _loaded;
    private ScrollViewer? _scroll;
    // Messages a scan just moved here, offered back until the tab is left.
    private (long ScanId, int Moved)? _undo;

    private SpamViewModel? Vm => DataContext as SpamViewModel;

    public SpamPage()
    {
        InitializeComponent();
        Focusable = true;

        if (IPlatformApplication.Current?.Services?.GetService(typeof(SpamViewModel)) is SpamViewModel viewModel)
        {
            DataContext = viewModel;
            viewModel.Items.CollectionChanged += OnItemsChanged;
            viewModel.Changed += UpdateScanBanner;
            _ = LoadAsync(viewModel);
        }

        SpamList.TemplateApplied += (_, e) => _scroll = e.NameScope.Find<ScrollViewer>("SpamScroll");
        Ui.RowPressEffect.Attach(SpamList, SpamHost);

        _scan = IPlatformApplication.Current?.Services?.GetService(typeof(SpamScanViewModel)) as SpamScanViewModel;
        if (_scan is not null)
        {
            _scan.Changed += UpdateScanBanner;
            _scan.Applied += (scanId, moved) =>
            {
                _undo = (scanId, moved);
                UpdateScanBanner();
            };
            _ = _scan.LoadPendingAsync();
        }
    }

    /// <summary>Opens the history scan over the tab.</summary>
    public void OpenScan()
    {
        ClearFocus();
        ScanView.OpenScan();
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

        _undo = null;
        UpdateScanBanner();

        // Back on the tab it starts from its top: menus and the search closed, the list at its first row.
        for (var layers = 0; layers < 4 && HandleBack(); layers++)
        {
        }

        if (_scroll is not null)
            _scroll.Offset = default;
    }

    /// <summary>True when it consumed the back press.</summary>
    public bool HandleBack()
    {
        if (Overlay.HandleBack() || ScanView.HandleBack())
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

    private void OnBackToInboxTapped(object? sender, TappedEventArgs e) => Ui.MainView.Current?.SelectMessagesTab();

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
        else if ((e.Source as StyledElement)?.DataContext is SpamBatchItem batch)
        {
            ClearFocus();
            ScanView.OpenBatch(batch.ScanId);
        }
    }

    private void OnScanLinkTapped(object? sender, TappedEventArgs e) => OpenScan();

    private void UpdateScanBanner()
    {
        if (_scan is null)
            return;

        var loc = LocalizationManager.Instance;
        (string Text, string Action)? banner = _scan.Stage switch
        {
            ScanStage.Scanning => (string.Format(loc["Scan_Running"], SpamItem.FormatPercent(_scan.Percent)), string.Empty),
            ScanStage.Review => (loc["Scan_Pending"], loc["Scan_Review"]),
            ScanStage.Applying => (loc["Scan_Moving"], string.Empty),
            _ when Vm is { RestoringLeft: > 0 } vm => (string.Format(loc["Scan_Restoring"], LocalizedNumbers.Number(vm.RestoringLeft)), string.Empty),
            _ when _undo is { } undo && Vm?.Batch(undo.ScanId) is not null => (string.Format(loc["Scan_Moved"], LocalizedNumbers.Number(undo.Moved)), loc["Scan_Undo"]),
            _ => null
        };

        ScanBanner.IsVisible = banner is not null;
        if (banner is { } shown)
        {
            ScanBannerText.Text = shown.Text;
            ScanBannerAction.Text = shown.Action;
            ScanBannerAction.IsVisible = shown.Action.Length > 0;
        }
    }

    private void OnScanBannerTapped(object? sender, TappedEventArgs e)
    {
        if (_scan?.Stage is ScanStage.Scanning or ScanStage.Review or ScanStage.Applying)
            OpenScan();
        else if (_undo is { } undo)
            ScanView.OpenBatch(undo.ScanId);
    }

    private async void OnScanBannerActionTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_scan?.Stage == ScanStage.Review)
        {
            OpenScan();
            return;
        }

        if (_undo is not { } undo || Vm is null)
            return;

        _undo = null;
        UpdateScanBanner();
        try
        {
            var batch = Vm.Batch(undo.ScanId);
            var total = batch?.Items.Count ?? 0;
            if (await Vm.RestoreBatchAsync(undo.ScanId, keep: false) < total)
            {
                var loc = LocalizationManager.Instance;
                await Ui.Dialogs.AlertAsync(string.Empty, loc["Spam_RestoreFailed"], loc["Spam_Ok"]);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Undoing the scan failed: {ex}");
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
