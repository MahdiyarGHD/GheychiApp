using Avalonia.Controls;
using Avalonia.Input;
using Gheychi.App.Platforms.Android;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

// In-chat search: the header bar, the match outlines and jumping between matches.
public partial class ChatView
{
    private bool _settingSearchText;
    private ChatViewModel? _searchVm;

    /// <summary>The user tapped the avatar or name in the header.</summary>
    public event Action? ProfileRequested;

    public bool IsSearching => Vm?.Search.IsActive == true;

    /// <summary>
    /// Opens the search bar. With a <paramref name="focusMessageId"/> (a result tapped in the main search) the list
    /// goes to that message; the keyboard stays down then so the matches are what the reader sees.
    /// </summary>
    public void OpenSearch(string? text = null, long focusMessageId = 0)
    {
        if (Vm is not { } vm)
            return;

        CloseSearch();
        _searchVm = vm;
        vm.Search.CurrentHitChanged += OnSearchHitChanged;

        SetSearchText(text ?? string.Empty);

        SearchHeader.IsVisible = true;
        vm.Search.Open(vm.Thread.ThreadId, text, focusMessageId);

        if (string.IsNullOrWhiteSpace(text))
            SearchEntry.Focus();
    }

    public void CloseSearch()
    {
        if (_searchVm is not { } vm)
            return;

        _searchVm = null;
        vm.Search.CurrentHitChanged -= OnSearchHitChanged;
        vm.Search.Close();

        SearchHeader.IsVisible = false;
        ReleaseInputFocus();
        SetSearchText(string.Empty);
    }

    private void SetSearchText(string text)
    {
        _settingSearchText = true;
        SearchEntry.Text = text;
        SearchClear.IsVisible = text.Length > 0;
        _settingSearchText = false;
    }

    private void OnHeaderTapped(object? sender, TappedEventArgs e) => ProfileRequested?.Invoke();

    private void OnHeaderCallTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is { } vm)
            ProfileLauncher.Dial(PhoneNumberNormalizer.ToSendAddress(vm.Thread.Phone));
    }

    private void OnHeaderMenuTapped(object? sender, TappedEventArgs e) => HeaderMenuOverlay.IsVisible = true;

    private void OnCloseHeaderMenuTapped(object? sender, TappedEventArgs e) => HeaderMenuOverlay.IsVisible = false;

    private void OnMenuSearchTapped(object? sender, TappedEventArgs e)
    {
        HeaderMenuOverlay.IsVisible = false;
        OpenSearch();
    }

    private void OnSearchBackTapped(object? sender, TappedEventArgs e) => CloseSearch();

    private void OnSearchOlderTapped(object? sender, TappedEventArgs e) => Vm?.Search.Older();

    private void OnSearchNewerTapped(object? sender, TappedEventArgs e) => Vm?.Search.Newer();

    private void OnSearchClearTapped(object? sender, TappedEventArgs e) => SearchEntry.Text = string.Empty;

    // The keyboard's search key steps to the next older match, like the arrows.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        Vm?.Search.Older();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = SearchEntry.Text ?? string.Empty;
        SearchClear.IsVisible = text.Length > 0;
        if (!_settingSearchText && _searchVm is { } vm)
            _ = vm.Search.SetQueryAsync(text);
    }

    private async void OnSearchHitChanged(ThreadSearchHit hit)
    {
        try
        {
            if (_searchVm is not { } vm)
                return;

            // Opening a chat scrolls to its bottom once the list settled; a jump made before that would be undone.
            for (var wait = 0; !_initialLayoutSettled && wait < 20; wait++)
                await Task.Delay(30);

            // A match from an older part of the history that was never scrolled to has to be loaded first.
            if (!await vm.EnsureLoadedThroughAsync(hit.Row))
                return;

            // The reader may have moved to another match while the history was loading.
            if (!ReferenceEquals(vm, _searchVm) || vm.Search.CurrentHit != hit)
                return;

            var message = vm.Messages.FirstOrDefault(m => m.Id == hit.MessageId);
            if (message is not null)
                ScrollToMessage(vm.Items.IndexOf(message));
        }
        catch (Exception ex)
        {
            // async void: a failed jump must not terminate the app.
            System.Diagnostics.Debug.WriteLine($"Jump to search match failed: {ex}");
        }
    }

    // Puts the message in the middle of the page so its neighbours are visible.
    private void ScrollToMessage(int index)
    {
        if (Vm is null || index < 0 || index >= Vm.Items.Count)
            return;

        _followTail = false;
        MessagesList.ScrollToIndex(index, RowAlignment.Center);
    }
}
