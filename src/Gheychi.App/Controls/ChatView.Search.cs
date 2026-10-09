using Avalonia.Controls;
using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.Ui;
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

    /// <summary>The menu asked for a list that lives on the profile page: the starred or the spam messages of the chat.</summary>
    public event Action<ProfileSection>? SectionRequested;

    /// <summary>The menu asked to archive the chat (true) or bring it back to the inbox (false).</summary>
    public event Action<bool>? ArchiveRequested;

    /// <summary>Tells whether a conversation is in the archive; set by the page that hosts the chat.</summary>
    public Func<long, bool>? IsArchivedLookup { get; set; }

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

    private void OnHeaderMenuTapped(object? sender, TappedEventArgs e)
    {
        PrepareHeaderMenu();
        HeaderMenuOverlay.IsVisible = true;
    }

    // What the menu offers depends on the conversation: archive or bring back, block or unblock.
    private void PrepareHeaderMenu()
    {
        if (Vm is not { } vm)
            return;

        var loc = LocalizationManager.Instance;
        var archived = IsArchivedLookup?.Invoke(vm.Thread.ThreadId) == true;
        MenuArchiveIcon.Data = IconCatalog.Find(archived ? "unarchive.png" : "archive.png");
        MenuArchiveLabel.Text = loc[archived ? "Profile_Unarchive" : "Profile_Archive"];

        var address = PhoneNumberNormalizer.ToSendAddress(vm.Thread.Phone);
        MenuBlockRow.IsVisible = _blocked is not null && !string.IsNullOrWhiteSpace(address);
        MenuBlockLabel.Text = loc[_blocked?.IsBlocked(address) == true ? "Profile_Unblock" : "Profile_Block"];
    }

    private void OnMenuDetailsTapped(object? sender, TappedEventArgs e)
    {
        HeaderMenuOverlay.IsVisible = false;
        if (Vm is { } vm)
            ProfileLauncher.ShowContact(PhoneNumberNormalizer.ToSendAddress(vm.Thread.Phone));
    }

    private void OnMenuArchiveTapped(object? sender, TappedEventArgs e)
    {
        HeaderMenuOverlay.IsVisible = false;
        if (Vm is { } vm)
            ArchiveRequested?.Invoke(IsArchivedLookup?.Invoke(vm.Thread.ThreadId) != true);
    }

    private async void OnMenuBlockTapped(object? sender, TappedEventArgs e)
    {
        HeaderMenuOverlay.IsVisible = false;
        try
        {
            if (_blocked is null || Vm is not { } vm)
                return;

            var address = PhoneNumberNormalizer.ToSendAddress(vm.Thread.Phone);
            if (string.IsNullOrWhiteSpace(address))
                return;

            if (await BlockActions.ToggleAsync(_blocked, address, vm.Thread.Name))
                RefreshBlocked();
        }
        catch (Exception ex)
        {
            // async void: a failed block must not terminate the app.
            System.Diagnostics.Debug.WriteLine($"Blocking the sender failed: {ex}");
        }
    }

    private void OnMenuStarredTapped(object? sender, TappedEventArgs e)
    {
        HeaderMenuOverlay.IsVisible = false;
        SectionRequested?.Invoke(ProfileSection.Starred);
    }

    private void OnMenuSpamTapped(object? sender, TappedEventArgs e)
    {
        HeaderMenuOverlay.IsVisible = false;
        SectionRequested?.Invoke(ProfileSection.Spam);
    }

    private void OnMenuHelpTapped(object? sender, TappedEventArgs e)
    {
        HeaderMenuOverlay.IsVisible = false;
        _ = HelpLink.OpenAsync();
    }

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

    /// <summary>Scrolls to a message that may be far back in the history; <paramref name="row"/> is its offset from the newest.</summary>
    public async Task JumpToMessageAsync(int row, long messageId)
    {
        if (Vm is not { } vm)
            return;

        for (var wait = 0; !_initialLayoutSettled && wait < 20; wait++)
            await Task.Delay(30);

        if (!await vm.EnsureLoadedThroughAsync(row) || !ReferenceEquals(vm, Vm))
            return;

        var message = vm.Messages.FirstOrDefault(m => m.Id == messageId);
        if (message is not null)
            ScrollToMessage(vm.Items.IndexOf(message));
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
