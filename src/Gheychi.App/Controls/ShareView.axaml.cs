using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

/// <summary>
/// Where a text shared from another app goes: the inbox's conversations, filtered as you type, and a typed number as
/// a place of its own.
/// </summary>
public partial class ShareView : UserControl
{
    private readonly FastObservableCollection<ThreadItem> _visible = [];
    private MessagesViewModel? _vm;
    private bool _open;
    private string _number = string.Empty;
    private long _lastTappedThreadId;
    private long _lastTappedThreadTick;

    public event EventHandler? BackRequested;
    public event EventHandler<ThreadItem>? ThreadChosen;
    public event EventHandler<string>? NumberChosen;

    public ShareView()
    {
        InitializeComponent();
        ShareList.ItemsSource = _visible;
        Ui.RowPressEffect.Attach(ShareList, ShareHost);
    }

    public void Initialize(MessagesViewModel vm)
    {
        if (ReferenceEquals(_vm, vm))
            return;

        if (_vm != null)
            _vm.Threads.CollectionChanged -= OnThreadsChanged;

        _vm = vm;
        _vm.Threads.CollectionChanged += OnThreadsChanged;
    }

    /// <summary>Starts a fresh pick for this text.</summary>
    public void Open(string? sharedText)
    {
        _open = true;
        FilterEntry.Text = string.Empty;
        SharedTextLabel.Text = sharedText?.Trim() ?? string.Empty;
        SharedTextLabel.IsVisible = SharedTextLabel.Text.Length > 0;
        RefreshVisible();
    }

    public void ResetState()
    {
        _open = false;
        Unfocus();
        FilterEntry.Text = string.Empty;
        MessagesViewModel.ApplyItems(_visible, []);
    }

    /// <returns>True when it consumed the back press.</returns>
    public bool HandleBack()
    {
        if (!FilterEntry.IsFocused)
            return false;

        Unfocus();
        return true;
    }

    public void Unfocus()
    {
        if (FilterEntry.IsFocused)
            Root.Focus();
    }

    private void OnThreadsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_open)
            RefreshVisible();
    }

    private void RefreshVisible()
    {
        if (_vm == null)
            return;

        var query = FilterEntry.Text?.Trim();
        List<ThreadItem> items;
        if (string.IsNullOrEmpty(query))
        {
            items = [.. _vm.Threads];
        }
        else
        {
            var needles = SearchTextHelper.BuildVariants(query);
            items = _vm.Threads
                .Where(t => SearchTextHelper.ContainsAny(t.Name, needles) ||
                            SearchTextHelper.ContainsAny(t.Phone, needles) ||
                            SearchTextHelper.ContainsAny(t.Preview, needles))
                .ToList();
        }

        MessagesViewModel.ApplyItems(_visible, items);
        ShowNumberRow(query);
        UpdateEmptyState(query);
    }

    private void ShowNumberRow(string? query)
    {
        var isNumber = LooksLikeNumber(query);
        _number = isNumber ? query! : string.Empty;
        NumberRow.IsVisible = isNumber;
        if (isNumber)
            NumberLabel.Text = string.Format(LocalizationManager.Instance["Compose_SendTo"], query);
    }

    private static bool LooksLikeNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var digits = 0;
        foreach (var c in text)
        {
            if (char.IsDigit(c))
                digits++;
            else if (c is not ('+' or ' ' or '-' or '(' or ')'))
                return false;
        }

        return digits >= 3;
    }

    private void UpdateEmptyState(string? query)
    {
        var empty = _visible.Count == 0 && !NumberRow.IsVisible;
        EmptyLabel.IsVisible = empty;
        if (empty)
            EmptyLabel.Text = LocalizationManager.Instance[string.IsNullOrEmpty(query) ? "Share_Empty" : "Archive_NoMatches"];
    }

    private void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearFilterButton.IsVisible = !string.IsNullOrEmpty(FilterEntry.Text);
        if (_open)
            RefreshVisible();
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        Unfocus();
    }

    private void OnClearFilterTapped(object? sender, TappedEventArgs e) => FilterEntry.Text = string.Empty;

    private void OnBackTapped(object? sender, TappedEventArgs e)
    {
        Unfocus();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnNumberTapped(object? sender, TappedEventArgs e)
    {
        if (_number.Length == 0)
            return;

        Unfocus();
        NumberChosen?.Invoke(this, _number);
    }

    // One handler for the whole list: a row's children inherit its data context.
    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is not ThreadItem thread)
            return;

        // A tap can be reported twice on some devices.
        var now = Environment.TickCount64;
        if (thread.ThreadId == _lastTappedThreadId && now - _lastTappedThreadTick < 300)
            return;
        _lastTappedThreadId = thread.ThreadId;
        _lastTappedThreadTick = now;

        Unfocus();
        ThreadChosen?.Invoke(this, thread);
    }
}
