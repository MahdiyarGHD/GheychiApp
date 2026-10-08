using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

public partial class ArchiveView : UserControl
{
    private readonly FastObservableCollection<ThreadItem> _visible = [];
    private MessagesViewModel? _vm;
    private bool _isSelectionMode;
    private bool _holdFired;
    private long _lastTappedThreadId;
    private long _lastTappedThreadTick;

    public event EventHandler? BackRequested;
    public event EventHandler<ThreadItem>? ThreadOpened;

    public ArchiveView()
    {
        InitializeComponent();
        ArchiveList.ItemsSource = _visible;

        // Releasing a finger that held a row also reports a tap; the next press starts a new gesture.
        ArchiveList.AddHandler(PointerPressedEvent, (_, _) => _holdFired = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        UpdateEmptyState();
    }

    public bool IsSelectionMode => _isSelectionMode;

    public void Initialize(MessagesViewModel vm)
    {
        if (ReferenceEquals(_vm, vm))
            return;

        if (_vm != null)
            _vm.ArchivedThreads.CollectionChanged -= OnArchivedChanged;

        _vm = vm;
        _vm.ArchivedThreads.CollectionChanged += OnArchivedChanged;
        RefreshVisible();
    }

    public void ResetState()
    {
        ExitSelectionMode();
        Unfocus();
        if (!string.IsNullOrEmpty(FilterEntry.Text))
            FilterEntry.Text = string.Empty;
    }

    // Returns true when it consumed the back press.
    public bool HandleBack()
    {
        if (_isSelectionMode)
        {
            ExitSelectionMode();
            return true;
        }

        if (FilterEntry.IsFocused)
        {
            Unfocus();
            return true;
        }

        return false;
    }

    // Avalonia has no "clear focus": focus goes to the page itself, which closes the keyboard.
    private void Unfocus()
    {
        if (FilterEntry.IsFocused)
            Root.Focus();
    }

    private void OnArchivedChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshVisible();

    private void RefreshVisible()
    {
        if (_vm == null)
            return;

        var query = FilterEntry.Text?.Trim();
        var source = _vm.ArchivedThreads;

        List<ThreadItem> items;
        if (string.IsNullOrEmpty(query))
        {
            items = [.. source];
        }
        else
        {
            var needles = SearchTextHelper.BuildVariants(query);
            items = source
                .Where(t => SearchTextHelper.ContainsAny(t.Name, needles) ||
                            SearchTextHelper.ContainsAny(t.Phone, needles) ||
                            SearchTextHelper.ContainsAny(t.Preview, needles))
                .ToList();
        }

        MessagesViewModel.ApplyItems(_visible, items);

        if (_isSelectionMode && !_visible.Any(t => t.IsSelected))
            ExitSelectionMode();
        else
            UpdateSelectionHeader();

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var empty = _visible.Count == 0;
        EmptyState.IsVisible = empty;
        ArchiveList.IsVisible = !empty;
        if (!empty)
            return;

        var filtering = !string.IsNullOrWhiteSpace(FilterEntry.Text);
        var loc = LocalizationManager.Instance;
        EmptyTitleLabel.Text = loc[filtering ? "Archive_NoMatches" : "Archive_EmptyTitle"];
        EmptyMessageLabel.Text = filtering ? string.Empty : loc["Archive_EmptyMessage"];
        EmptyMessageLabel.IsVisible = !filtering;
    }

    private void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearFilterButton.IsVisible = !string.IsNullOrEmpty(FilterEntry.Text);
        RefreshVisible();
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        Unfocus();
    }

    private void OnClearFilterTapped(object? sender, TappedEventArgs e)
    {
        FilterEntry.Text = string.Empty;
    }

    private void OnBackTapped(object? sender, TappedEventArgs e)
    {
        Unfocus();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    // One handler for the whole list: a row's children inherit its data context.
    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (_holdFired || (e.Source as StyledElement)?.DataContext is not ThreadItem thread)
            return;

        // A tap can be reported twice on some devices.
        var now = Environment.TickCount64;
        if (thread.ThreadId == _lastTappedThreadId && now - _lastTappedThreadTick < 300)
            return;
        _lastTappedThreadId = thread.ThreadId;
        _lastTappedThreadTick = now;

        if (_isSelectionMode)
        {
            thread.IsSelected = !thread.IsSelected;
            if (!_visible.Any(t => t.IsSelected))
                ExitSelectionMode();
            else
                UpdateSelectionHeader();
            return;
        }

        Unfocus();
        ThreadOpened?.Invoke(this, thread);
    }

    private void OnListHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || (e.Source as StyledElement)?.DataContext is not ThreadItem thread)
            return;

        _holdFired = true;
        _lastTappedThreadId = thread.ThreadId;
        _lastTappedThreadTick = Environment.TickCount64;

        thread.IsSelected = !thread.IsSelected;
        _isSelectionMode = _visible.Any(t => t.IsSelected);
        UpdateSelectionHeader();
    }

    private void UpdateSelectionHeader()
    {
        NormalHeader.IsVisible = !_isSelectionMode;
        SelectionHeader.IsVisible = _isSelectionMode;
        SelectedCountLabel.Text = _visible.Count(t => t.IsSelected).ToString();
    }

    private void ExitSelectionMode()
    {
        if (_vm != null)
        {
            foreach (var t in _vm.ArchivedThreads)
                t.IsSelected = false;
        }

        _isSelectionMode = false;
        UpdateSelectionHeader();
    }

    private void OnCancelSelectionTapped(object? sender, TappedEventArgs e) => ExitSelectionMode();

    private async void OnUnarchiveTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_vm == null)
                return;

            var selected = _visible.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0)
                return;

            _isSelectionMode = false;
            await _vm.UnarchiveThreadsAsync(selected);
            UpdateSelectionHeader();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unarchive failed: {ex}");
        }
    }
}
