using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Gheychi.App.Controls;
using Gheychi.App.Gestures;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.Pages;

public partial class MessagesPage : UserControl, IPageSwipeClient
{
    private const double RowHeight = 68;
    private const int RecentSuggestionCount = 5;

    private const int ArchiveZIndex = 1;
    private const int ComposeZIndex = 2;
    // Under the chat, so a result opens its chat straight over the results (search and compose are never open together).
    private const int SearchZIndex = 2;
    private const int OverlayZIndex = 3;
    private const int ProfileZIndex = 4;
    private const double InboxParallax = 0.25;
    private const double FlingVelocity = 250;
    private const double CommitProgress = 0.22;

    private bool _animating;
    private long _lastScrollTime;
    private int _lastFirstVisible = -1;
    private int _lastLastVisible = -1;
    private bool _timerRunning;
    private long _lastTappedThreadId;
    private long _lastTappedThreadTick;
    private bool _overlaysWarmedUp;
    private bool _inboxShown;
    private bool _composeBusy;
    private bool _archiveOpen;
    private bool _swipeDragging;
    private ScrollViewer? _threadsScroll;

    private ChatView? _chatOverlay;
    private SearchView? _searchOverlay;
    private ComposeView? _composeOverlay;
    private ArchiveView? _archiveOverlay;
    private ProfileView? _profileOverlay;
    private SpamOverlay? _spamPopup;

    private MessagesViewModel? Vm => DataContext as MessagesViewModel;

    // ChatView (1000+ lines of XAML) and SearchView are only needed once the user opens them; they are built when first
    // needed or by the warm-up, so the first inbox frame does not wait for them.
    private ChatView ChatOverlay => _chatOverlay ??= CreateChatOverlay();
    private SearchView SearchOverlay => _searchOverlay ??= CreateSearchOverlay();
    private ComposeView ComposeOverlay => _composeOverlay ??= CreateComposeOverlay();
    private ArchiveView ArchiveOverlay => _archiveOverlay ??= CreateArchiveOverlay();
    private ProfileView ProfileOverlay => _profileOverlay ??= CreateProfileOverlay();
    private bool IsChatClosed => _chatOverlay is null || !_chatOverlay.IsVisible;
    private bool IsSearchClosed => _searchOverlay is null || !_searchOverlay.IsVisible;
    private bool IsComposeClosed => _composeOverlay is null || !_composeOverlay.IsVisible;
    private bool IsProfileClosed => _profileOverlay is null || !_profileOverlay.IsVisible;

    private static Panel Overlays => MainView.Current!.Overlays;

    private static ISmsService? SmsService => IPlatformApplication.Current?.Services.GetService<ISmsService>();

    public MessagesPage()
        : this(null)
    {
    }

    public MessagesPage(MessagesViewModel? vm)
    {
        // Resolve the view model first: it starts loading the cached inbox on a worker thread while the XAML below is built.
        var viewModel = vm ?? IPlatformApplication.Current?.Services.GetService<MessagesViewModel>() ?? new MessagesViewModel();
        InitializeComponent();
        DataContext = viewModel;

        ThreadsList.TemplateApplied += (_, e) =>
        {
            _threadsScroll = e.NameScope.Find<ScrollViewer>("ThreadsScroll");
            if (_threadsScroll is not null)
                _threadsScroll.ScrollChanged += OnThreadsScrolled;
        };

        // Opened from a notification on a cold start: show the chat, not an inbox that is about to be covered.
        if (ChatLaunchRequests.HasPending)
        {
            InboxLayer.IsVisible = false;
            _ = ChatOverlay;
        }
    }

    // ---- Overlay creation -----------------------------------------------------------------------

    private ChatView CreateChatOverlay()
    {
        var chat = new ChatView { IsVisible = false, ZIndex = OverlayZIndex };
        chat.BackRequested += CloseChatAsync;
        chat.ProfileRequested += OnChatProfileRequested;
        Overlays.Children.Add(chat);
        return chat;
    }

    private SearchView CreateSearchOverlay()
    {
        var search = new SearchView { IsVisible = false, ZIndex = SearchZIndex };
        search.BackRequested += OnCloseSearchRequested;
        search.SearchResultTapped += OnSearchResultTapped;
        Overlays.Children.Add(search);
        return search;
    }

    private ComposeView CreateComposeOverlay()
    {
        var compose = new ComposeView { IsVisible = false, ZIndex = ComposeZIndex };
        compose.BackRequested += (_, _) => _ = CloseComposeAsync();
        compose.RecipientChosen += OnComposeRecipientChosen;
        Overlays.Children.Add(compose);
        return compose;
    }

    private ArchiveView CreateArchiveOverlay()
    {
        var archive = new ArchiveView { IsVisible = false, ZIndex = ArchiveZIndex };
        archive.BackRequested += OnArchiveBackRequested;
        archive.ThreadOpened += OnArchiveThreadOpened;
        if (Vm != null)
            archive.Initialize(Vm);
        Overlays.Children.Add(archive);
        return archive;
    }

    private ProfileView CreateProfileOverlay()
    {
        var profile = new ProfileView { IsVisible = false, ZIndex = ProfileZIndex };
        profile.BackRequested += (_, _) => _ = CloseProfileAsync();
        profile.TextRequested += OnProfileTextRequested;
        profile.SearchRequested += OnProfileSearchRequested;
        profile.ArchiveRequested += OnProfileArchiveRequested;
        profile.SpamMenuRequested += (_, item) => SpamPopup.ShowMenu(item);
        Overlays.Children.Add(profile);
        return profile;
    }

    // The profile's spam list opens the same restore/report popup the Spam tab uses, above everything else.
    private SpamOverlay SpamPopup => _spamPopup ??= CreateSpamPopup();

    private SpamOverlay CreateSpamPopup()
    {
        var popup = new SpamOverlay { ZIndex = ProfileZIndex + 1 };
        Overlays.Children.Add(popup);
        return popup;
    }

    // ---- Tab lifecycle --------------------------------------------------------------------------

    private bool IsDefaultAppGateShown => NotDefaultScreen.IsVisible;

    // Back from the default-app prompt the answer is known only once the app is in front again.
    private void UpdateDefaultAppGate()
    {
        var gated = SmsService is { } sms && !sms.IsDefaultSmsApp();
        NotDefaultScreen.IsVisible = gated;
        ThreadsList.IsVisible = !gated;
    }

    private void OnAppResumed() => MainThread.BeginInvokeOnMainThread(UpdateDefaultAppGate);

    private void OnSetDefaultTapped(object? sender, TappedEventArgs e) => _ = SmsService?.EnsureDefaultSmsAppAsync();

    private void OnNotDefaultTapEater(object? sender, TappedEventArgs e) => e.Handled = true;

    /// <summary>The Messages tab came to the front.</summary>
    public async void OnShown()
    {
        _inboxShown = true;
        UpdateDefaultAppGate();
        MainActivity.Resumed -= OnAppResumed;
        MainActivity.Resumed += OnAppResumed;
        PageSwipe.Client = this;
        ChatLaunchRequests.Requested -= OnChatLaunchRequested;
        ChatLaunchRequests.Requested += OnChatLaunchRequested;

        // A tapped notification gets its chat before the inbox starts loading: the chat view is the slow part to
        // build, and the inbox behind it only has to be ready by the time the chat is closed. While the activity is
        // still resuming this is left to OnChatLaunchRequested.
        if (ChatPresence.IsAppVisible)
            await OpenRequestedChatAsync();

        try
        {
            if (Vm != null)
                await Vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            // async void: a failed permission request or snapshot read must not terminate the app.
            System.Diagnostics.Debug.WriteLine($"Inbox initialize failed: {ex}");
        }

        if (!_overlaysWarmedUp)
        {
            _overlaysWarmedUp = true;
            _ = WarmUpOverlaysAsync();
        }
    }

    /// <summary>Another tab came to the front.</summary>
    public void OnHidden()
    {
        _inboxShown = false;
        MainActivity.Resumed -= OnAppResumed;
        ChatLaunchRequests.Requested -= OnChatLaunchRequested;
        if (ReferenceEquals(PageSwipe.Client, this))
            PageSwipe.Client = null;
    }

    // Build the overlays once the inbox is on screen, so the first chat/search/compose open does not pay for building
    // their views. Each is built in one UI-thread step, so they are built one at a time, only while nobody is touching
    // the screen, with a pause in between.
    private async Task WarmUpOverlaysAsync()
    {
        try
        {
            await Task.Delay(2500);
            await WaitForInboxQuietAsync();
            await WarmAsync(ChatOverlay, () => IsChatClosed);

            await Task.Delay(1000);
            await WaitForInboxQuietAsync();
            await WarmAsync(SearchOverlay, () => IsSearchClosed);

            await Task.Delay(1000);
            await WaitForInboxQuietAsync();
            if (Vm is not null)
                ComposeOverlay.SetRecent(BuildRecentRows());
            await ComposeOverlay.PreloadContactsAsync();
            await WarmAsync(ComposeOverlay, () => IsComposeClosed);

            await Task.Delay(1000);
            await WaitForInboxQuietAsync();
            await WarmAsync(ArchiveOverlay, () => !_archiveOpen && !_swipeDragging);

            await Task.Delay(1000);
            await WaitForInboxQuietAsync();
            await WarmAsync(ProfileOverlay, () => IsProfileClosed);

            // Reading chats ahead is background work that would compete with the screens above.
            await Task.Delay(1500);
            await WaitForQuietAsync();
            Vm?.EnableChatPreload();

            await Task.Delay(1500);
            await WaitForQuietAsync();
            if (IPlatformApplication.Current?.Services.GetService<Gheychi.Core.Spam.ISpamClassifier>() is { } spamClassifier)
                await Task.Run(() => spamClassifier.WarmUpAsync());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warming up the overlays failed: {ex}");
        }
    }

    // A parked overlay is collapsed, so measure and layout skip it. It is shown once, invisibly, so that its first
    // measure, layout and row creation happen now and not on the tap that opens it.
    private static async Task WarmAsync(Control overlay, Func<bool> isParked)
    {
        if (!isParked())
            return;

        overlay.Opacity = 0;
        overlay.IsHitTestVisible = false;
        overlay.IsVisible = true;
        await Task.Delay(250);
        if (isParked())
        {
            overlay.IsVisible = false;
            OverlayAnimator.SetTranslation(overlay, 0, 0);
        }

        overlay.Opacity = 1;
        overlay.IsHitTestVisible = true;
    }

    // While an overlay covers the whole screen the inbox under it is collapsed too: typing in a chat must not lay out
    // the inbox list behind it.
    private void CoverInbox()
    {
        if (!IsChatClosed || !IsSearchClosed || !IsComposeClosed)
            InboxLayer.IsVisible = false;
    }

    private void UncoverInbox() => InboxLayer.IsVisible = true;

    private async Task WaitForQuietAsync()
    {
        while (!IsQuiet)
            await Task.Delay(200);
    }

    private bool IsQuiet => Environment.TickCount64 - _lastScrollTime >= 500 && UserActivity.IsIdle(700);

    // Building a screen is one long UI-thread step. Done while another tab is up, it froze the tab the user had just opened.
    private async Task WaitForInboxQuietAsync()
    {
        while (!_inboxShown || !IsQuiet)
            await Task.Delay(200);
    }

    // ---- Inbox list -----------------------------------------------------------------------------

    private void OnThreadsScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_threadsScroll is not { } scroll)
            return;

        _lastFirstVisible = Math.Max(0, (int)(scroll.Offset.Y / RowHeight));
        _lastLastVisible = (int)((scroll.Offset.Y + scroll.Viewport.Height) / RowHeight);
        _lastScrollTime = Environment.TickCount64;

        if (!_timerRunning)
        {
            // Background chat warm-up reads SMS/SQLite and allocates heavily; keep it off the CPU for the duration of a scroll burst.
            ChatViewModel.CancelPreload();
            _timerRunning = true;
            _ = CheckScrollIdleAsync();
        }
    }

    private async Task CheckScrollIdleAsync()
    {
        while (true)
        {
            await Task.Delay(350);
            if (Environment.TickCount64 - _lastScrollTime >= 350)
            {
                _timerRunning = false;
                OnScrollSettled();
                break;
            }
        }
    }

    private void OnScrollSettled()
    {
        if (Vm is not { ChatPreloadEnabled: true } vm || vm.Threads.Count == 0 || _lastFirstVisible < 0)
            return;

        var start = Math.Max(0, _lastFirstVisible - 2);
        var end = Math.Min(vm.Threads.Count - 1, _lastLastVisible + 2);
        var count = end - start + 1;
        if (count <= 0)
            return;

        var visible = vm.Threads.Skip(start).Take(count).ToList();
        var smsService = IPlatformApplication.Current?.Services.GetService<ISmsService>();
        var dateFormatter = IPlatformApplication.Current?.Services.GetService<IDateFormattingService>();
        if (smsService != null && dateFormatter != null)
            ChatViewModel.PreloadVisibleThreads(visible, smsService, dateFormatter);
    }

    private static ThreadItem? ThreadFrom(RoutedEventArgs e) =>
        (e.Source as StyledElement)?.DataContext as ThreadItem;

    private void OnThreadsTapped(object? sender, TappedEventArgs e)
    {
        if (_animating || !IsChatClosed || ThreadFrom(e) is not { } thread)
            return;

        if (Vm?.IsSelectionMode == true)
            ToggleSelectionSafely(thread);
        else
            _ = OpenChatSafely(thread);
    }

    private void OnThreadsHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || _animating || !IsChatClosed || Vm == null || ThreadFrom(e) is not { } thread)
            return;

        if (!Vm.IsSelectionMode)
            Vm.EnterSelectionMode(thread);
        else
            ToggleSelectionSafely(thread);
    }

    private void ToggleSelectionSafely(ThreadItem thread)
    {
        var now = Environment.TickCount64;
        if (thread.ThreadId == _lastTappedThreadId && now - _lastTappedThreadTick < 250)
            return;

        _lastTappedThreadTick = now;
        _lastTappedThreadId = thread.ThreadId;

        Vm?.ToggleThreadSelection(thread);
    }

    private Task OpenChatSafely(ThreadItem thread, string? searchText = null, long focusMessageId = 0)
    {
        var now = Environment.TickCount64;
        if (thread.ThreadId == _lastTappedThreadId && now - _lastTappedThreadTick < 300)
            return Task.CompletedTask;

        _lastTappedThreadTick = now;
        _lastTappedThreadId = thread.ThreadId;

        var wasUnread = thread.IsUnread;
        // Captured before MarkAsRead zeroes it: the fetch widens its first page to cover every
        // unread message so the divider lands before the first one.
        var unreadCount = thread.Count;
        if (wasUnread)
            thread.MarkAsRead();

        return OpenChatAsync(thread, wasUnread, unreadCount, searchText: searchText, focusMessageId: focusMessageId);
    }

    // ---- Notification launch --------------------------------------------------------------------

    // Posted, not run inline: it is raised from inside the activity's OnResume, and the open has to start after that returns.
    private void OnChatLaunchRequested() =>
        MainThread.BeginInvokeOnMainThread(() => _ = OpenRequestedChatAsync());

    /// <summary>Opens the conversation a tapped notification asked for.</summary>
    private async Task OpenRequestedChatAsync()
    {
        var request = ChatLaunchRequests.Take();
        if (request is null)
            return;

        try
        {
            if (!IsProfileClosed)
                ParkProfileOverlay();

            if (!IsChatClosed)
            {
                if (ChatOverlay.Vm?.Thread.ThreadId == request.ThreadId)
                    return;

                await CloseChatAsync();
            }

            // Building the chat view is the slow part; the messages are being read meanwhile.
            _ = ChatOverlay;

            int unread;
            try
            {
                unread = await request.Prepared;
            }
            catch (Exception)
            {
                unread = 0;
            }

            var thread = FindThread(request.ThreadId)
                         ?? ThreadItem.ForConversation(request.ThreadId, request.SubId, request.Name, request.Address);
            thread.Count = unread;
            thread.IsUnread = unread > 0;

            var wasUnread = thread.IsUnread;
            if (wasUnread)
                thread.MarkAsRead();
            await OpenChatAsync(thread, wasUnread, unread, animate: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open notified chat failed: {ex}");
        }
        finally
        {
            RevealInbox();
        }
    }

    /// <summary>Ends the cold-start hold (see the constructor); harmless when nothing was held.</summary>
    private void RevealInbox()
    {
        if (IsChatClosed)
            UncoverInbox();
    }

    private ThreadItem? FindThread(long threadId) =>
        Vm?.Threads.FirstOrDefault(t => t.ThreadId == threadId)
        ?? Vm?.ArchivedThreads.FirstOrDefault(t => t.ThreadId == threadId);

    // ---- Selection mode -------------------------------------------------------------------------

    private void OnExitSelectionModeTapped(object? sender, TappedEventArgs e)
    {
        SelectBoxOverlay.IsVisible = false;
        Vm?.ExitSelectionMode();
    }

    private async void OnDeleteSelectedThreadsTapped(object? sender, TappedEventArgs e)
    {
        SelectBoxOverlay.IsVisible = false;
        if (Vm == null || Vm.SelectedCount == 0)
            return;

        var loc = Localization.LocalizationManager.Instance;
        var count = Vm.SelectedCount;
        string title;
        string message;

        if (count == 1)
        {
            title = loc["Messages_DeleteSingleConfirmTitle"];
            message = loc["Messages_DeleteSingleConfirmMessage"];
        }
        else
        {
            title = string.Format(loc["Messages_DeleteMultipleConfirmTitle"], count);
            message = string.Format(loc["Messages_DeleteMultipleConfirmMessage"], count);
        }

        var confirmed = await Dialogs.AlertAsync(title, message, loc["Chat_DeleteConfirmButton"], loc["Chat_Cancel"]);
        if (confirmed)
            await Vm.DeleteSelectedThreadsAsync();
    }

    private async void OnArchiveSelectedThreadsTapped(object? sender, TappedEventArgs e)
    {
        SelectBoxOverlay.IsVisible = false;
        if (Vm == null || Vm.SelectedCount == 0)
            return;

        await Vm.ArchiveSelectedThreadsAsync();
    }

    private void OnThreeDotsTapped(object? sender, TappedEventArgs e) =>
        SelectBoxOverlay.IsVisible = !SelectBoxOverlay.IsVisible;

    private void OnCloseSelectBoxTapped(object? sender, TappedEventArgs e) => SelectBoxOverlay.IsVisible = false;

    private void OnSelectBoxTapEater(object? sender, TappedEventArgs e) => e.Handled = true;

    private async void OnMarkAsUnreadTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        SelectBoxOverlay.IsVisible = false;
        if (Vm == null || Vm.SelectedCount == 0)
            return;

        await Vm.MarkSelectedAsUnreadAsync();
    }

    // ---- Chat -----------------------------------------------------------------------------------

    private double OverlayDistance =>
        MainView.Current is { Bounds.Height: > 0 } main ? main.Bounds.Height + 50 : 850;

    private void Park(Control overlay)
    {
        overlay.IsVisible = false;
        OverlayAnimator.SetTranslation(overlay, 0, OverlayAnimator.ParkedDistance);
    }

    /// <param name="searchText">Opens the chat with its search already running for this text (a result tapped in the main search).</param>
    /// <param name="focusMessageId">The match the search starts on.</param>
    private async Task OpenChatAsync(
        ThreadItem thread, bool wasUnread = false, int unreadCount = 0, SimCardInfo? sim = null, bool animate = true,
        string? searchText = null, long focusMessageId = 0)
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            // Yield SQLite to the opening chat immediately: stop list warm-up
            // queries so page 1 + its history prefetch run uncontended.
            ChatViewModel.CancelPreload();

            // Only cheap UI work before the slide: the chat is reset to its skeleton and the page is read
            // on a worker thread meanwhile.
            var chat = ChatOverlay;
            var cached = chat.PrepareForTransition(thread);
            if (sim is not null)
                chat.Vm?.ChooseSim(sim.SlotIndex, sim.SubId);
            var distance = OverlayDistance;
            OverlayAnimator.SetTranslation(chat, 0, animate ? distance : 0);
            chat.IsVisible = true;
            ChatPresence.ChatOpened(thread.ThreadId);

            var fetchTask = cached is not null
                ? Task.FromResult<PreparedChatData?>(cached)
                : chat.FetchMessagesAsync(thread, unreadHint: unreadCount);

            if (animate)
            {
                await OverlayAnimator.SettleAsync();
                await OverlayAnimator.SlideYAsync(chat, distance, 0, OverlayAnimator.OpenDuration, decelerate: true);
            }

            CoverInbox();

            // The list is filled once the chat has arrived. Binding the rows before the slide delays its start and
            // binding them during it takes UI-thread time away from the frames around it.
            var preparedData = await fetchTask;
            if (preparedData is not null)
                chat.ApplyMessages(preparedData);
            else
                chat.HideSkeleton();

            // Last, so the jump to the match is not undone by the chat settling at its bottom.
            if (!string.IsNullOrWhiteSpace(searchText))
                chat.OpenSearch(searchText, focusMessageId);

            if (wasUnread)
            {
                var smsService = SmsService;
                _ = Task.Run(async () =>
                {
                    if (smsService != null)
                        await smsService.MarkThreadAsReadAsync(thread.ThreadId);

                    // The cached page still carries the unread divider; reopening must not replay it.
                    ChatViewModel.EvictCache(thread.ThreadId);
                });
            }
        }
        catch (InvalidOperationException)
        {
            OverlayAnimator.SetTranslation(ChatOverlay, 0, 0);
            ChatOverlay.IsVisible = true;
        }
        finally
        {
            _animating = false;
        }
    }

    private async Task CloseChatAsync()
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            await ChatOverlay.Close();
            UncoverInbox();

            await OverlayAnimator.SlideYAsync(ChatOverlay, 0, OverlayDistance, OverlayAnimator.CloseDuration, decelerate: false);
            ParkChatOverlay();

            // Replies sent, messages read or deleted inside the chat are not in the list yet.
            if (Vm != null)
                _ = Vm.LoadThreadsAsync();
        }
        catch (InvalidOperationException)
        {
            ParkChatOverlay();
            UncoverInbox();
        }
        finally
        {
            _animating = false;
        }
    }

    private void ParkChatOverlay()
    {
        ChatPresence.ChatClosed();
        Park(ChatOverlay);
        ChatOverlay.ResetAfterClose();
    }

    // ---- Profile --------------------------------------------------------------------------------

    private async void OnChatProfileRequested()
    {
        try
        {
            if (_animating || IsChatClosed || !IsProfileClosed || ChatOverlay.Vm is null)
                return;

            await OpenProfileAsync(ChatOverlay.Vm.Thread);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open profile failed: {ex}");
        }
    }

    private async Task OpenProfileAsync(ThreadItem thread)
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            var profile = ProfileOverlay;
            ChatOverlay.ReleaseInputFocus();
            profile.Bind(thread, IsArchived(thread.ThreadId));

            var distance = OverlayDistance;
            OverlayAnimator.SetTranslation(profile, 0, distance);
            profile.IsVisible = true;

            await OverlayAnimator.SettleAsync();
            await OverlayAnimator.SlideYAsync(profile, distance, 0, OverlayAnimator.OpenDuration, decelerate: true);
            profile.OnOpened();
        }
        catch (InvalidOperationException)
        {
            OverlayAnimator.SetTranslation(ProfileOverlay, 0, 0);
            ProfileOverlay.IsVisible = true;
        }
        finally
        {
            _animating = false;
        }
    }

    private async Task CloseProfileAsync()
    {
        if (_animating || IsProfileClosed)
            return;
        _animating = true;
        try
        {
            await OverlayAnimator.SlideYAsync(ProfileOverlay, 0, OverlayDistance, OverlayAnimator.CloseDuration, decelerate: false);
            ParkProfileOverlay();
        }
        catch (InvalidOperationException)
        {
            ParkProfileOverlay();
        }
        finally
        {
            _animating = false;
        }
    }

    private void ParkProfileOverlay()
    {
        Park(ProfileOverlay);
        ProfileOverlay.Reset();
    }

    private bool IsArchived(long threadId) => Vm?.ArchivedThreads.Any(t => t.ThreadId == threadId) == true;

    private async void OnProfileTextRequested(object? sender, EventArgs e)
    {
        try
        {
            await CloseProfileAsync();
            ChatOverlay.FocusMessageInput();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profile text action failed: {ex}");
        }
    }

    private async void OnProfileSearchRequested(object? sender, EventArgs e)
    {
        try
        {
            await CloseProfileAsync();
            ChatOverlay.OpenSearch();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profile search action failed: {ex}");
        }
    }

    private async void OnProfileArchiveRequested(object? sender, bool archive)
    {
        try
        {
            if (Vm is null || ChatOverlay.Vm?.Thread is not { } thread)
                return;

            if (archive)
                await Vm.ArchiveThreadAsync(thread);
            else
                await Vm.UnarchiveThreadsAsync([thread]);

            // The conversation is no longer where the user opened it from: back to the inbox.
            await CloseProfileAsync();
            await CloseChatAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profile archive action failed: {ex}");
        }
    }

    // ---- Search ---------------------------------------------------------------------------------

    private async void OnSearchBarTapped(object? sender, TappedEventArgs e)
    {
        if (_animating || !IsChatClosed || Vm?.IsSelectionMode == true)
            return;

        await OpenSearchAsync();
    }

    private async Task OpenSearchAsync()
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            // The overlay keeps its view model: a new one per open re-bound the whole screen (every list
            // and binding) and re-read the recent searches and SIMs right before the slide. Park clears it.
            var search = SearchOverlay;
            search.Initialize();
            var distance = OverlayDistance;
            OverlayAnimator.SetTranslation(search, 0, distance);
            search.IsVisible = true;

            await OverlayAnimator.SettleAsync();
            await OverlayAnimator.SlideYAsync(search, distance, 0, OverlayAnimator.OpenDuration, decelerate: true);
            CoverInbox();
            search.FocusSearchInput();
        }
        catch (InvalidOperationException)
        {
            OverlayAnimator.SetTranslation(SearchOverlay, 0, 0);
            SearchOverlay.IsVisible = true;
        }
        finally
        {
            _animating = false;
        }
    }

    private async Task CloseSearchAsync()
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            // The overlay leaves with what it shows; it is cleared once it is out of sight.
            SearchOverlay.UnfocusSearchInput();
            UncoverInbox();

            await OverlayAnimator.SlideYAsync(SearchOverlay, 0, OverlayDistance, OverlayAnimator.CloseDuration, decelerate: false);
            ParkSearchOverlay();
        }
        catch (InvalidOperationException)
        {
            ParkSearchOverlay();
            UncoverInbox();
        }
        finally
        {
            _animating = false;
        }
    }

    private void ParkSearchOverlay()
    {
        Park(SearchOverlay);
        SearchOverlay.Reset();
    }

    private void OnCloseSearchRequested(object? sender, EventArgs e) => _ = CloseSearchAsync();

    private async void OnSearchResultTapped(object? sender, SearchResultItem item)
    {
        if (_animating || !IsChatClosed)
            return;

        var thread = Vm?.Threads.FirstOrDefault(t => t.ThreadId == item.ThreadId);
        if (thread == null)
        {
            thread = new ThreadItem
            {
                ThreadId = item.ThreadId,
                SubId = item.SubId,
                Name = item.DisplayName,
                Phone = item.Address,
                Initials = item.Initials,
                IconFile = ThreadItem.DetectIcon(item.DisplayName, item.Address),
                Count = item.IsUnread ? 1 : 0,
                TotalCount = item.TotalMatches,
                Time = item.Time,
                Preview = item.SnippetRuns.FirstOrDefault()?.Text ?? string.Empty,
                IsUnread = item.IsUnread
            };
        }

        // The chat slides in over the results instead of waiting for them to slide away first; the
        // search is parked under it afterwards, so closing the chat still returns to the inbox.
        SearchOverlay.UnfocusSearchInput();

        // A result that matched the message text opens its chat with the same search running, on that message,
        // so the reader sees every other match in context. A result that matched only the name just opens the chat.
        var matchedText = item.MessageId > 0 && !string.IsNullOrWhiteSpace(item.QueryText);
        try
        {
            await OpenChatSafely(thread, matchedText ? item.QueryText : null, matchedText ? item.MessageId : 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open chat from search failed: {ex}");
        }

        if (!IsSearchClosed && !IsChatClosed)
            ParkSearchOverlay();
    }

    // ---- Compose --------------------------------------------------------------------------------

    private async void OnComposeTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_animating || !IsChatClosed || !IsSearchClosed || !IsComposeClosed || Vm?.IsSelectionMode == true)
                return;

            await OpenComposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open compose failed: {ex}");
        }
    }

    private async Task OpenComposeAsync()
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            var compose = ComposeOverlay;
            var distance = OverlayDistance;
            compose.PrepareForOpen(BuildRecentRows(), distance);
            compose.IsVisible = true;

            await OverlayAnimator.SettleAsync();
            await compose.SlideAsync(open: true, distance);
            CoverInbox();
            compose.OnOpened();
        }
        catch (InvalidOperationException)
        {
            OverlayAnimator.SetTranslation(ComposeOverlay, 0, 0);
            ComposeOverlay.IsVisible = true;
        }
        finally
        {
            _animating = false;
        }
    }

    private async Task CloseComposeAsync()
    {
        if (_animating || IsComposeClosed)
            return;
        _animating = true;
        try
        {
            var compose = ComposeOverlay;
            compose.PrepareForClose();
            UncoverInbox();

            await compose.SlideAsync(open: false, OverlayDistance);
            compose.Park();
        }
        catch (InvalidOperationException)
        {
            ComposeOverlay.Park();
            UncoverInbox();
        }
        finally
        {
            _animating = false;
        }
    }

    // The newest conversations with real phone numbers (not banks, not operator shortcodes): the people
    // most likely to be messaged again, offered above the address book.
    private List<ComposeRow> BuildRecentRows()
    {
        var rows = new List<ComposeRow>(RecentSuggestionCount);
        if (Vm is null)
            return rows;

        foreach (var thread in Vm.Threads)
        {
            var address = PhoneNumberNormalizer.ToSendAddress(thread.Phone);
            if (!ContactListBuilder.IsPersonalNumber(address) || rows.Any(r => r.Address == address))
                continue;

            var named = !string.Equals(thread.Name, thread.Phone, StringComparison.Ordinal);
            rows.Add(new ComposeRow
            {
                Kind = ComposeRowKind.Contact,
                Name = thread.Name,
                ContactName = named ? thread.Name : null,
                Address = address,
                Initials = named ? thread.Initials : string.Empty,
                Subtitle = named ? thread.Phone : string.Empty
            });

            if (rows.Count == RecentSuggestionCount)
                break;
        }

        return rows;
    }

    private async void OnComposeRecipientChosen(object? sender, ComposeRecipient recipient)
    {
        if (_composeBusy || _animating || !IsChatClosed)
            return;
        _composeBusy = true;
        try
        {
            // The keyboard starts hiding while the conversation is looked up.
            ComposeOverlay.PrepareForClose();

            var address = PhoneNumberNormalizer.ToSendAddress(recipient.Address);
            var threadId = SmsService is null ? 0 : await SmsService.GetOrCreateThreadIdAsync(address);
            if (threadId <= 0)
            {
                await Dialogs.AlertAsync(string.Empty, Localization.LocalizationManager.Instance["Compose_OpenFailed"], "OK");
                return;
            }

            // An existing conversation (inbox or archive) keeps its name, unread state and SIM.
            var thread = Vm?.Threads.FirstOrDefault(t => t.ThreadId == threadId)
                         ?? Vm?.ArchivedThreads.FirstOrDefault(t => t.ThreadId == threadId)
                         ?? CreateNewThreadItem(threadId, recipient);

            var wasUnread = thread.IsUnread;
            var unreadCount = thread.Count;
            if (wasUnread)
                thread.MarkAsRead();

            await OpenChatAsync(thread, wasUnread, unreadCount, recipient.Sim);

            // The chat now covers the compose screen; closing it returns to the inbox.
            ComposeOverlay.Park();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open conversation failed: {ex}");
        }
        finally
        {
            _composeBusy = false;
        }
    }

    private static ThreadItem CreateNewThreadItem(long threadId, ComposeRecipient recipient)
    {
        var phone = PhoneNumberNormalizer.FormatDisplay(recipient.Address);
        var name = string.IsNullOrWhiteSpace(recipient.ContactName) ? phone : recipient.ContactName;
        return new ThreadItem
        {
            ThreadId = threadId,
            Name = name,
            Phone = phone,
            Initials = ThreadItem.GenerateInitials(name),
            IconFile = ThreadItem.DetectIcon(name, recipient.Address),
            Time = string.Empty,
            Preview = string.Empty
        };
    }

    // ---- Archive --------------------------------------------------------------------------------

    private void OnArchiveBackRequested(object? sender, EventArgs e) => _ = CloseArchiveAsync();

    private void OnArchiveThreadOpened(object? sender, ThreadItem thread)
    {
        if (_animating || !IsChatClosed)
            return;

        _ = OpenChatSafely(thread);
    }

    // Finger direction that opens the archive: toward the leading edge's opposite side, so the
    // archive page sits past the trailing edge and mirrors in right-to-left languages.
    private static int OpenSwipeSign =>
        Localization.CultureService.GetFlowDirection() == FlowDirection.RightToLeft ? 1 : -1;

    private double PageWidth => Bounds.Width > 0 ? Bounds.Width : 360;

    public int AllowedSwipeSign
    {
        get
        {
            if (_animating || !IsChatClosed || !IsSearchClosed || !IsComposeClosed || SelectBoxOverlay.IsVisible || Vm?.IsSelectionMode == true
                || IsDefaultAppGateShown)
                return 0;

            if (_archiveOpen)
                return _archiveOverlay?.IsSelectionMode == true ? 0 : -OpenSwipeSign;

            return OpenSwipeSign;
        }
    }

    public void OnSwipeStarted()
    {
        _swipeDragging = true;
        var archive = ArchiveOverlay;
        if (Vm != null)
            archive.Initialize(Vm);
        archive.IsHitTestVisible = false;
        SetArchiveProgress(_archiveOpen ? 1 : 0);
        archive.IsVisible = true;
    }

    public void OnSwipeMoved(double offsetDp)
    {
        if (!_swipeDragging)
            return;

        SetArchiveProgress(ProgressFor(offsetDp));
    }

    public void OnSwipeEnded(double offsetDp, double velocityDpPerSecond, bool cancelled)
    {
        if (!_swipeDragging)
            return;
        _swipeDragging = false;

        var wasOpen = _archiveOpen;
        var progress = ProgressFor(offsetDp);

        // Positive when the finger is moving the way the current gesture is heading.
        var heading = wasOpen ? -OpenSwipeSign : OpenSwipeSign;
        var toward = velocityDpPerSecond * heading;

        bool open;
        if (cancelled)
            open = wasOpen;
        else if (!wasOpen)
            open = toward > FlingVelocity || (toward > -FlingVelocity && progress > CommitProgress);
        else
            open = !(toward > FlingVelocity || (toward > -FlingVelocity && progress < 1 - CommitProgress));

        _ = SettleArchiveAsync(open, progress, Math.Abs(velocityDpPerSecond));
    }

    private double ProgressFor(double offsetDp)
    {
        var width = PageWidth;
        if (_archiveOpen)
            return 1 - Math.Clamp(offsetDp * -OpenSwipeSign / width, 0, 1);

        return Math.Clamp(offsetDp * OpenSwipeSign / width, 0, 1);
    }

    private void SetArchiveProgress(double progress)
    {
        var width = PageWidth;
        OverlayAnimator.SetTranslation(ArchiveOverlay, -OpenSwipeSign * width * (1 - progress), 0);
        OverlayAnimator.SetTranslation(InboxLayer, OpenSwipeSign * width * InboxParallax * progress, 0);
    }

    private async Task SettleArchiveAsync(bool open, double fromProgress, double velocity)
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            var width = PageWidth;
            var targetProgress = open ? 1.0 : 0.0;
            var remainingDp = Math.Abs(targetProgress - fromProgress) * width;
            var duration = TimeSpan.FromMilliseconds(Math.Clamp(remainingDp / Math.Max(velocity, 1100) * 1000, 110, 220));

            if (!open)
                ArchiveOverlay.IsHitTestVisible = false;

            // Both pages move with the same easing, as they do under the finger.
            var archiveFrom = -OpenSwipeSign * width * (1 - fromProgress);
            var archiveTo = -OpenSwipeSign * width * (1 - targetProgress);
            var inboxFrom = OpenSwipeSign * width * InboxParallax * fromProgress;
            var inboxTo = OpenSwipeSign * width * InboxParallax * targetProgress;
            await Task.WhenAll(
                OverlayAnimator.SlideXAsync(ArchiveOverlay, archiveFrom, archiveTo, duration, decelerate: true),
                OverlayAnimator.SlideXAsync(InboxLayer, inboxFrom, inboxTo, duration, decelerate: true));

            _archiveOpen = open;
            if (open)
            {
                ArchiveOverlay.IsHitTestVisible = true;
            }
            else
            {
                ArchiveOverlay.ResetState();
                if (!_swipeDragging)
                    Park(ArchiveOverlay);
                ArchiveOverlay.IsHitTestVisible = true;
            }
        }
        catch (InvalidOperationException)
        {
            _archiveOpen = open;
            SetArchiveProgress(open ? 1 : 0);
        }
        finally
        {
            _animating = false;
        }
    }

    private async Task CloseArchiveAsync()
    {
        if (!_archiveOpen)
            return;

        // Same bookkeeping as a swipe that ends on the closed side.
        _swipeDragging = false;
        await SettleArchiveAsync(false, 1, 0);
    }

    // ---- Back -----------------------------------------------------------------------------------

    /// <returns>True when the back press was used up by an open overlay, menu or selection.</returns>
    public bool HandleBack()
    {
        if (!IsProfileClosed)
        {
            if (_spamPopup?.HandleBack() == true)
                return true;

            if (_profileOverlay?.HandleBack() != true)
                MainThread.BeginInvokeOnMainThread(async () => await CloseProfileAsync());
            return true;
        }

        if (_archiveOpen && IsChatClosed && IsSearchClosed && IsComposeClosed)
        {
            if (_archiveOverlay?.HandleBack() != true)
                MainThread.BeginInvokeOnMainThread(async () => await CloseArchiveAsync());
            return true;
        }

        if (SelectBoxOverlay.IsVisible)
        {
            SelectBoxOverlay.IsVisible = false;
            return true;
        }

        if (!IsChatClosed)
        {
            if (!ChatOverlay.HandleBack())
                MainThread.BeginInvokeOnMainThread(async () => await CloseChatAsync());
            return true;
        }

        if (!IsSearchClosed)
        {
            if (!SearchOverlay.HandleBack())
                MainThread.BeginInvokeOnMainThread(async () => await CloseSearchAsync());
            return true;
        }

        if (!IsComposeClosed)
        {
            if (!ComposeOverlay.HandleBack())
                MainThread.BeginInvokeOnMainThread(async () => await CloseComposeAsync());
            return true;
        }

        if (Vm?.IsSelectionMode == true)
        {
            Vm.ExitSelectionMode();
            return true;
        }

        return false;
    }
}
