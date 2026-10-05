using Gheychi.App.Controls;
using Gheychi.App.Gestures;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;

namespace Gheychi.App.Pages;

public partial class MessagesPage : ContentPage, IThreadRowHost, IPageSwipeClient
{
    private bool _animating;
    private long _lastScrollTime;
    private int _lastFirstVisible = -1;
    private int _lastLastVisible = -1;
    private bool _timerRunning;
    private long _lastTappedThreadId;
    private long _lastTappedThreadTick;
    private bool _overlaysWarmedUp;
    private ChatView? _chatOverlay;
    private SearchView? _searchOverlay;
    private ComposeView? _composeOverlay;
    private bool _composeBusy;
    private ArchiveView? _archiveOverlay;
    private bool _archiveOpen;
    private bool _swipeDragging;
    private MessagesViewModel? Vm => BindingContext as MessagesViewModel;

    // ChatView (1000+ lines of XAML) and SearchView are only needed once the user opens them;
    // building them in the constructor delayed the first inbox frame.
    private ChatView ChatOverlay => _chatOverlay ??= CreateChatOverlay();
    private SearchView SearchOverlay => _searchOverlay ??= CreateSearchOverlay();
    private ComposeView ComposeOverlay => _composeOverlay ??= CreateComposeOverlay();
    private ArchiveView ArchiveOverlay => _archiveOverlay ??= CreateArchiveOverlay();
    private bool IsChatClosed => _chatOverlay is null || _chatOverlay.InputTransparent;
    private bool IsSearchClosed => _searchOverlay is null || _searchOverlay.InputTransparent;
    private bool IsComposeClosed => _composeOverlay is null || _composeOverlay.InputTransparent;

    public MessagesPage(MessagesViewModel? vm = null)
    {
        // Resolve the view model first: it starts loading the cached inbox on a worker thread
        // while the XAML below is inflated.
        var viewModel = vm ?? IPlatformApplication.Current?.Services.GetService<MessagesViewModel>() ?? new MessagesViewModel();
        InitializeComponent();
        BindingContext = viewModel;

        // Opened from a notification on a cold start: show the chat, not an inbox that is about to be covered.
        // The chat view is built now instead of after the first frame, and the inbox and tab bar stay out
        // of sight until the chat is up.
        if (ChatLaunchRequests.HasPending)
        {
            InboxLayer.IsVisible = false;
            Shell.SetTabBarIsVisible(this, false);
            _ = ChatOverlay;
        }
    }

    private ChatView CreateChatOverlay()
    {
        var chat = new ChatView
        {
            IsVisible = true,
            InputTransparent = true,
            TranslationY = 3000,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        chat.BackRequested += CloseChatAsync;
        chat.ZIndex = OverlayZIndex;
        RootGrid.Children.Add(chat);
        return chat;
    }

    private SearchView CreateSearchOverlay()
    {
        var search = new SearchView
        {
            IsVisible = true,
            InputTransparent = true,
            TranslationY = 3000,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        search.BackRequested += OnCloseSearchRequested;
        search.SearchResultTapped += OnSearchResultTapped;
        search.ZIndex = OverlayZIndex;
        RootGrid.Children.Add(search);
        return search;
    }

    private ArchiveView CreateArchiveOverlay()
    {
        var archive = new ArchiveView
        {
            IsVisible = true,
            InputTransparent = true,
            TranslationX = -OpenSwipeSign * OffscreenDistance,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        archive.BackRequested += OnArchiveBackRequested;
        archive.ThreadOpened += OnArchiveThreadOpened;
        if (Vm != null)
            archive.Initialize(Vm);

        // Explicit z-order: above the inbox, under the chat and search overlays that open on top of it.
        archive.ZIndex = ArchiveZIndex;
        RootGrid.Children.Add(archive);
        return archive;
    }

    private void OnArchiveBackRequested(object? sender, EventArgs e) => _ = CloseArchiveAsync();

    private void OnArchiveThreadOpened(object? sender, ThreadItem thread)
    {
        if (_animating || !IsChatClosed)
            return;

        OpenChatSafely(thread);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ChatLaunchRequests.Requested -= OnChatLaunchRequested;
        if (ReferenceEquals(PageSwipe.Client, this))
            PageSwipe.Client = null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        PageSwipe.Client = this;
        ChatLaunchRequests.Requested -= OnChatLaunchRequested;
        ChatLaunchRequests.Requested += OnChatLaunchRequested;

        // A tapped notification gets its chat before the inbox starts loading: the chat view is the slow
        // part to build, and the inbox behind it only has to be ready by the time the chat is closed.
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

    // Build the overlays once the inbox is on screen, so the first chat/search/compose open does not pay
    // the XAML inflation cost. Each one is hundreds of views created in a single UI-thread step, so they
    // are built one at a time, only while nobody is touching the screen, with a pause in between.
    private async Task WarmUpOverlaysAsync()
    {
        try
        {
            await Task.Delay(2500);
            await WaitForQuietAsync();
            _ = ChatOverlay;

            await Task.Delay(1000);
            await WaitForQuietAsync();
            _ = SearchOverlay;

            await Task.Delay(1000);
            await WaitForQuietAsync();
            _ = ComposeOverlay.PreloadContactsAsync();

            await Task.Delay(1000);
            await WaitForQuietAsync();
            _ = ArchiveOverlay;

            // Reading chats ahead is background work that would compete with the screens above.
            await Task.Delay(1500);
            await WaitForQuietAsync();
            Vm?.EnableChatPreload();
        }
        catch
        {
        }
    }

    private async Task WaitForQuietAsync()
    {
        while (Environment.TickCount64 - _lastScrollTime < 500 || !UserActivity.IsIdle(700))
            await Task.Delay(200);
    }

    private void OnThreadsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _lastFirstVisible = e.FirstVisibleItemIndex;
        _lastLastVisible = e.LastVisibleItemIndex;
        _lastScrollTime = Environment.TickCount64;

        if (!_timerRunning)
        {
            // Background chat warm-up reads SMS/SQLite and allocates heavily; keep it off the
            // CPU for the duration of a scroll burst.
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
            var elapsed = Environment.TickCount64 - _lastScrollTime;
            if (elapsed >= 350)
            {
                _timerRunning = false;
                OnScrollSettled();
                break;
            }
        }
    }

    private void OnScrollSettled()
    {
        if (Vm is not { ChatPreloadEnabled: true })
            return;

        if (Vm == null || Vm.Threads.Count == 0 || _lastFirstVisible < 0)
            return;

        var start = Math.Max(0, _lastFirstVisible - 2);
        var end = Math.Min(Vm.Threads.Count - 1, _lastLastVisible + 2);
        var count = end - start + 1;
        if (count <= 0)
            return;

        var visible = Vm.Threads.Skip(start).Take(count).ToList();
        var smsService = IPlatformApplication.Current?.Services.GetService<Gheychi.Core.Services.ISmsService>();
        var dateFormatter = IPlatformApplication.Current?.Services.GetService<Gheychi.Core.Services.IDateFormattingService>();
        if (smsService != null && dateFormatter != null)
        {
            ChatViewModel.PreloadVisibleThreads(visible, smsService, dateFormatter);
        }
    }

    private void OnThreadRowTapped(object? sender, EventArgs e)
    {
        if (_animating || !IsChatClosed)
            return;

        if ((sender as View)?.BindingContext is ThreadItem thread)
        {
            if (Vm?.IsSelectionMode == true)
            {
                ToggleSelectionSafely(thread);
            }
            else
            {
                OpenChatSafely(thread);
            }
        }
    }

    public void OnThreadRowTappedDirect(ThreadItem thread)
    {
        if (_animating || !IsChatClosed)
            return;

        if (Vm?.IsSelectionMode == true)
        {
            ToggleSelectionSafely(thread);
        }
        else
        {
            OpenChatSafely(thread);
        }
    }

    public void OnThreadRowHeldDirect(ThreadItem thread)
    {
        if (_animating || !IsChatClosed || Vm == null)
            return;

        if (!Vm.IsSelectionMode)
        {
            Vm.EnterSelectionMode(thread);
        }
        else
        {
            ToggleSelectionSafely(thread);
        }
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

    private void OpenChatSafely(ThreadItem thread)
    {
        var now = Environment.TickCount64;
        if (thread.ThreadId == _lastTappedThreadId && now - _lastTappedThreadTick < 300)
            return;

        _lastTappedThreadTick = now;
        _lastTappedThreadId = thread.ThreadId;

        var wasUnread = thread.IsUnread;
        // Captured before MarkAsRead zeroes it: the fetch widens its first page to cover every
        // unread message so the divider lands before the first one.
        var unreadCount = thread.Count;
        if (wasUnread)
        {
            thread.MarkAsRead();
        }
        _ = OpenChatAsync(thread, wasUnread, unreadCount);
    }

    private void OnChatLaunchRequested() => _ = OpenRequestedChatAsync();

    /// <summary>Opens the conversation a tapped notification asked for.</summary>
    private async Task OpenRequestedChatAsync()
    {
        var request = ChatLaunchRequests.Take();
        if (request is null)
            return;

        try
        {
            if (!IsChatClosed)
            {
                if (ChatOverlay.Vm?.Thread.ThreadId == request.ThreadId)
                    return;

                await CloseChatAsync();
            }

            // Building the chat view is the slow part; the messages are being read meanwhile.
            await EnsureOverlayReadyAsync(ChatOverlay);

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
        InboxLayer.IsVisible = true;
        if (IsChatClosed)
            Shell.SetTabBarIsVisible(this, true);
    }

    private ThreadItem? FindThread(long threadId) =>
        Vm?.Threads.FirstOrDefault(t => t.ThreadId == threadId)
        ?? Vm?.ArchivedThreads.FirstOrDefault(t => t.ThreadId == threadId);

    private void OnExitSelectionModeTapped(object? sender, EventArgs e)
    {
        SelectBoxOverlay.IsVisible = false;
        Vm?.ExitSelectionMode();
    }

    private async void OnDeleteSelectedThreadsTapped(object? sender, EventArgs e)
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

        var confirmed = await DisplayAlert(
            title,
            message,
            loc["Chat_DeleteConfirmButton"],
            loc["Chat_Cancel"]);

        if (confirmed)
        {
            await Vm.DeleteSelectedThreadsAsync();
        }
    }

    private async void OnArchiveSelectedThreadsTapped(object? sender, EventArgs e)
    {
        SelectBoxOverlay.IsVisible = false;
        if (Vm == null || Vm.SelectedCount == 0)
            return;

        await Vm.ArchiveSelectedThreadsAsync();
    }

    private void OnThreeDotsTapped(object? sender, EventArgs e)
    {
        SelectBoxOverlay.IsVisible = !SelectBoxOverlay.IsVisible;
    }

    private void OnCloseSelectBoxTapped(object? sender, EventArgs e)
    {
        SelectBoxOverlay.IsVisible = false;
    }

    private void OnSelectBoxTapEater(object? sender, EventArgs e)
    {
    }

    private async void OnMarkAsUnreadTapped(object? sender, EventArgs e)
    {
        SelectBoxOverlay.IsVisible = false;
        if (Vm == null || Vm.SelectedCount == 0)
            return;

        await Vm.MarkSelectedAsUnreadAsync();
    }

    private static async Task EnsureOverlayReadyAsync(View overlay)
    {
        if (overlay.IsLoaded && overlay.Handler?.PlatformView is not null)
            return;

        var loaded = new TaskCompletionSource();
        void OnLoaded(object? sender, EventArgs e) => loaded.TrySetResult();
        overlay.Loaded += OnLoaded;
        try
        {
            await Task.WhenAny(loaded.Task, Task.Delay(500));
        }
        finally
        {
            overlay.Loaded -= OnLoaded;
        }
    }

    private async Task OpenChatAsync(ThreadItem thread, bool wasUnread = false, int unreadCount = 0, SimCardInfo? sim = null, bool animate = true)
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
            if (_chatOverlay is null)
                await EnsureOverlayReadyAsync(ChatOverlay);

            // Yield SQLite to the opening chat immediately: stop list warm-up
            // queries so page 1 + its history prefetch run uncontended.
            ChatViewModel.CancelPreload();

            // Everything that needs the UI thread happens while nothing moves yet.
            var cacheHit = ChatOverlay.PrepareForTransition(thread);
            if (sim is not null)
                ChatOverlay.Vm?.SelectSim(sim.SlotIndex, sim.SubId);
            var distance = GetFallbackHeight();
            OverlayAnimator.SetTranslationY(ChatOverlay, animate ? distance : 0);
            ChatOverlay.InputTransparent = false;
            ChatPresence.ChatOpened(thread.ThreadId);
            Shell.SetTabBarIsVisible(this, false);

            Task<PreparedChatData?>? fetchTask = null;
            if (!cacheHit)
                fetchTask = Task.Run(() => ChatOverlay.FetchMessagesAsync(thread, unreadHint: unreadCount));

            // The tab bar disappearing and a freshly filled list make the page lay out again; let that
            // finish (two frames) before the slide starts instead of during it.
            var slide = Task.CompletedTask;
            if (animate)
            {
                await Task.Delay(30);
                slide = OverlayAnimator.SlideYAsync(ChatOverlay, distance, 0, 260, decelerate: true);
            }

            if (fetchTask is not null)
            {
                // Show the messages as soon as they are read; the slide is on the render thread and
                // keeps moving while the list is built.
                var preparedData = await fetchTask;
                if (preparedData is not null)
                    ChatOverlay.ApplyMessages(preparedData);
            }

            await slide;

            if (cacheHit && ChatOverlay.BindingContext is ChatViewModel cachedVm)
                ChatOverlay.ScrollToInitialPosition(cachedVm.FirstUnreadIndex);

            if (wasUnread)
            {
                var smsService = IPlatformApplication.Current?.Services.GetService<Gheychi.Core.Services.ISmsService>();
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
            OverlayAnimator.SetTranslationY(ChatOverlay, 0);
            Shell.SetTabBarIsVisible(this, false);
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
            _ = RestoreTabBarSoonAsync();

            await OverlayAnimator.SlideYAsync(ChatOverlay, 0, GetFallbackHeight(), 220, decelerate: false);
            ParkChatOverlay();

            // Replies sent, messages read or deleted inside the chat are not in the list yet.
            if (Vm != null)
                _ = Vm.LoadThreadsAsync();
        }
        catch (InvalidOperationException)
        {
            ParkChatOverlay();
            Shell.SetTabBarIsVisible(this, true);
        }
        finally
        {
            _animating = false;
        }
    }

    private void ParkChatOverlay()
    {
        ChatPresence.ChatClosed();
        ChatOverlay.InputTransparent = true;
        OverlayAnimator.SetTranslationY(ChatOverlay, OverlayAnimator.ParkedDistance);
        ChatOverlay.ResetAfterClose();
    }

    private static double GetFallbackHeight()
    {
        var density = DeviceDisplay.Current.MainDisplayInfo.Density;
        if (density > 0)
            return (DeviceDisplay.Current.MainDisplayInfo.Height / density) + 50;
        return 850;
    }

    private async void OnSearchBarTapped(object? sender, EventArgs e)
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
            if (_searchOverlay is null)
                await EnsureOverlayReadyAsync(SearchOverlay);

            var searchVm = IPlatformApplication.Current?.Services.GetService<SearchViewModel>() ?? new SearchViewModel();
            SearchOverlay.Initialize(searchVm);
            var distance = GetFallbackHeight();
            OverlayAnimator.SetTranslationY(SearchOverlay, distance);
            SearchOverlay.InputTransparent = false;
            Shell.SetTabBarIsVisible(this, false);

            await Task.Delay(30);
            await OverlayAnimator.SlideYAsync(SearchOverlay, distance, 0, 260, decelerate: true);
            SearchOverlay.FocusSearchInput();
        }
        catch (InvalidOperationException)
        {
            OverlayAnimator.SetTranslationY(SearchOverlay, 0);
            Shell.SetTabBarIsVisible(this, false);
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
            _ = RestoreTabBarSoonAsync();

            await OverlayAnimator.SlideYAsync(SearchOverlay, 0, GetFallbackHeight(), 220, decelerate: false);
            ParkSearchOverlay();
        }
        catch (InvalidOperationException)
        {
            ParkSearchOverlay();
            Shell.SetTabBarIsVisible(this, true);
        }
        finally
        {
            _animating = false;
        }
    }

    private void ParkSearchOverlay()
    {
        SearchOverlay.InputTransparent = true;
        OverlayAnimator.SetTranslationY(SearchOverlay, OverlayAnimator.ParkedDistance);
        SearchOverlay.Reset();
    }

    private void OnCloseSearchRequested(object? sender, EventArgs e)
    {
        _ = CloseSearchAsync();
    }

    private async void OnSearchResultTapped(object? sender, SearchResultItem item)
    {
        if (_animating || !IsChatClosed)
            return;

        var thread = Vm?.Threads.FirstOrDefault(t => t.ThreadId == item.ThreadId);
        if (thread == null)
        {
            var now = DateTime.Now;
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
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
                Preview = item.FormattedSnippet.Spans.FirstOrDefault()?.Text ?? string.Empty,
                IsUnread = item.IsUnread
            };
        }

        await CloseSearchAsync();
        OpenChatSafely(thread);
    }

    private ComposeView CreateComposeOverlay()
    {
        var compose = new ComposeView
        {
            IsVisible = true,
            InputTransparent = true,
            TranslationY = OffscreenDistance,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        compose.BackRequested += (_, _) => _ = CloseComposeAsync();
        compose.RecipientChosen += OnComposeRecipientChosen;

        // Above the inbox and archive, under the chat that opens from it.
        compose.ZIndex = ComposeZIndex;
        RootGrid.Children.Add(compose);
        return compose;
    }

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
            if (_composeOverlay is null)
                await EnsureOverlayReadyAsync(ComposeOverlay);

            var compose = ComposeOverlay;
            var distance = GetFallbackHeight();
            compose.PrepareForOpen(BuildRecentRows(), distance);
            compose.InputTransparent = false;
            Shell.SetTabBarIsVisible(this, false);

            await compose.SlideAsync(open: true, distance);
            compose.OnOpened();
        }
        catch (InvalidOperationException)
        {
            ComposeOverlay.SetTranslationY(0);
            Shell.SetTabBarIsVisible(this, false);
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
            _ = RestoreTabBarSoonAsync();

            await compose.SlideAsync(open: false, GetFallbackHeight());
            compose.Park();
        }
        catch (InvalidOperationException)
        {
            ComposeOverlay.Park();
            Shell.SetTabBarIsVisible(this, true);
        }
        finally
        {
            _animating = false;
        }
    }

    // The tab bar coming back resizes the page; waiting a moment keeps that out of the first frames of the slide.
    private async Task RestoreTabBarSoonAsync()
    {
        await Task.Delay(60);
        Shell.SetTabBarIsVisible(this, true);
    }

    private const int RecentSuggestionCount = 5;

    // The newest conversations with real phone numbers (not banks, not operator shortcodes): the people
    // most likely to be messaged again, offered above the address book.
    private List<ComposeRow> BuildRecentRows()
    {
        var rows = new List<ComposeRow>(RecentSuggestionCount);
        if (Vm is null)
            return rows;

        foreach (var thread in Vm.Threads)
        {
            var address = Gheychi.Core.Services.PhoneNumberNormalizer.ToSendAddress(thread.Phone);
            if (!Gheychi.Core.Services.ContactListBuilder.IsPersonalNumber(address) || rows.Any(r => r.Address == address))
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

            var smsService = IPlatformApplication.Current?.Services.GetService<Gheychi.Core.Services.ISmsService>();
            var address = Gheychi.Core.Services.PhoneNumberNormalizer.ToSendAddress(recipient.Address);
            var threadId = smsService is null ? 0 : await smsService.GetOrCreateThreadIdAsync(address);
            if (threadId <= 0)
            {
                await DisplayAlertAsync(string.Empty, Localization.LocalizationManager.Instance["Compose_OpenFailed"], "OK");
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
        var phone = Gheychi.Core.Services.PhoneNumberNormalizer.FormatDisplay(recipient.Address);
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

    private const int ArchiveZIndex = 1;
    private const int ComposeZIndex = 2;
    private const int OverlayZIndex = 3;
    private const double OffscreenDistance = 3000;
    private const double InboxParallax = 0.25;
    private const double FlingVelocity = 250;
    private const double CommitProgress = 0.22;

    // Finger direction that opens the archive: toward the leading edge's opposite side, so the
    // archive page sits past the trailing edge and mirrors in right-to-left languages.
    private static int OpenSwipeSign =>
        Localization.CultureService.GetFlowDirection() == FlowDirection.RightToLeft ? 1 : -1;

    private double PageWidth => Width > 0 ? Width : DeviceDisplay.Current.MainDisplayInfo.Width / Math.Max(1, DeviceDisplay.Current.MainDisplayInfo.Density);

    public int AllowedSwipeSign
    {
        get
        {
            if (_animating || !IsChatClosed || !IsSearchClosed || !IsComposeClosed || SelectBoxOverlay.IsVisible || Vm?.IsSelectionMode == true)
                return 0;

            if (_archiveOpen)
                return _archiveOverlay?.IsSelectionMode == true ? 0 : -OpenSwipeSign;

            return OpenSwipeSign;
        }
    }

    public void OnSwipeStarted()
    {
        _swipeDragging = true;
        if (Vm != null)
            ArchiveOverlay.Initialize(Vm);
        ArchiveOverlay.InputTransparent = true;
        SetArchiveProgress(_archiveOpen ? 1 : 0);
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

    // The archive overlay's native view ignored later TranslationX updates from the MAUI property
    // (stayed at its initial offscreen offset), so the native translation is written directly too.
    private void SetArchiveX(double x)
    {
        var archive = ArchiveOverlay;
        archive.TranslationX = x;
#if ANDROID
        if (archive.Handler?.PlatformView is Android.Views.View native)
            native.TranslationX = (float)(x * DeviceDisplay.Current.MainDisplayInfo.Density);
#endif
    }

    private void SetArchiveProgress(double progress)
    {
        var width = PageWidth;
        SetArchiveX(-OpenSwipeSign * width * (1 - progress));
        InboxLayer.TranslationX = OpenSwipeSign * width * InboxParallax * progress;
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
            var duration = (uint)Math.Clamp(remainingDp / Math.Max(velocity, 900) * 1000, 140, 300);

            if (!open)
                ArchiveOverlay.InputTransparent = true;

            // Drives both pages through SetArchiveProgress, the same path the finger drag uses.
            var finished = new TaskCompletionSource();
            new Animation(SetArchiveProgress, fromProgress, targetProgress, Easing.CubicOut)
                .Commit(this, "ArchiveSettle", 16, duration, finished: (_, _) => finished.TrySetResult());
            await finished.Task;
            SetArchiveProgress(targetProgress);

            _archiveOpen = open;
            if (open)
            {
                ArchiveOverlay.InputTransparent = false;
            }
            else
            {
                SetArchiveX(-OpenSwipeSign * OffscreenDistance);
                ArchiveOverlay.ResetState();
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

    protected override bool OnBackButtonPressed()
    {
        if (_archiveOpen && IsChatClosed && IsSearchClosed && IsComposeClosed)
        {
            if (_archiveOverlay?.HandleBack() == true)
                return true;

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
            if (ChatOverlay.HandleBack())
                return true;

            MainThread.BeginInvokeOnMainThread(async () => await CloseChatAsync());
            return true;
        }

        if (!IsSearchClosed)
        {
            if (SearchOverlay.Vm?.IsInSearchResultsMode == true)
            {
                SearchOverlay.ResetSearchQueryAndFilter();
                return true;
            }

            MainThread.BeginInvokeOnMainThread(async () => await CloseSearchAsync());
            return true;
        }

        if (!IsComposeClosed)
        {
            if (ComposeOverlay.HandleBack())
                return true;

            MainThread.BeginInvokeOnMainThread(async () => await CloseComposeAsync());
            return true;
        }

        if (Vm?.IsSelectionMode == true)
        {
            Vm.ExitSelectionMode();
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
