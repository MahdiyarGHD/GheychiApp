using Gheychi.App.Controls;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Pages;

public partial class MessagesPage : ContentPage
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
    private MessagesViewModel? Vm => BindingContext as MessagesViewModel;

    // ChatView (1000+ lines of XAML) and SearchView are only needed once the user opens them;
    // building them in the constructor delayed the first inbox frame.
    private ChatView ChatOverlay => _chatOverlay ??= CreateChatOverlay();
    private SearchView SearchOverlay => _searchOverlay ??= CreateSearchOverlay();
    private bool IsChatClosed => _chatOverlay is null || _chatOverlay.InputTransparent;
    private bool IsSearchClosed => _searchOverlay is null || _searchOverlay.InputTransparent;

    public MessagesPage(MessagesViewModel? vm = null)
    {
        // Resolve the view model first: it starts loading the cached inbox on a worker thread
        // while the XAML below is inflated.
        var viewModel = vm ?? IPlatformApplication.Current?.Services.GetService<MessagesViewModel>() ?? new MessagesViewModel();
        InitializeComponent();
        BindingContext = viewModel;
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
        RootGrid.Children.Add(search);
        return search;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Vm != null)
            await Vm.InitializeAsync();

        if (!_overlaysWarmedUp)
        {
            _overlaysWarmedUp = true;
            _ = WarmUpOverlaysAsync();
        }
    }

    // Build the overlays once the inbox is on screen and the user is not scrolling, so the first
    // chat/search open does not pay the XAML inflation cost.
    private async Task WarmUpOverlaysAsync()
    {
        try
        {
            await Task.Delay(1500);
            await WaitForScrollIdleAsync();
            _ = ChatOverlay;

            await Task.Delay(400);
            await WaitForScrollIdleAsync();
            _ = SearchOverlay;
        }
        catch
        {
        }
    }

    private async Task WaitForScrollIdleAsync()
    {
        while (Environment.TickCount64 - _lastScrollTime < 500)
            await Task.Delay(250);
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
        if (wasUnread)
        {
            thread.MarkAsRead();
        }
        _ = OpenChatAsync(thread, wasUnread);
    }

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

    private async Task OpenChatAsync(ThreadItem thread, bool wasUnread = false)
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
            var cacheHit = ChatOverlay.PrepareForTransition(thread);
            var offscreenY = Height > 0 ? Height : GetFallbackHeight();
            ChatOverlay.TranslationY = offscreenY;
            ChatOverlay.InputTransparent = false;
            Shell.SetTabBarIsVisible(this, false);

            Task<PreparedChatData?>? fetchTask = null;
            if (!cacheHit)
                fetchTask = Task.Run(() => ChatOverlay.FetchMessagesAsync(thread));

            await ChatOverlay.TranslateToAsync(0, 0, 220, Easing.CubicOut);

            if (fetchTask is not null)
            {
                var preparedData = await fetchTask;
                if (preparedData is not null)
                    ChatOverlay.ApplyMessages(preparedData);
            }
            else if (cacheHit && ChatOverlay.BindingContext is ChatViewModel cachedVm)
            {
                ChatOverlay.ScrollToInitialPosition(cachedVm.FirstUnreadIndex);
            }

            if (wasUnread)
            {
                var smsService = IPlatformApplication.Current?.Services.GetService<Gheychi.Core.Services.ISmsService>();
                _ = Task.Run(() => smsService?.MarkThreadAsReadAsync(thread.ThreadId));
            }
        }
        catch (InvalidOperationException)
        {
            ChatOverlay.TranslationY = 0;
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
            var offscreenY = Height > 0 ? Height : GetFallbackHeight();
            var slideTask = ChatOverlay.TranslateToAsync(0, offscreenY, 220, Easing.CubicIn);

            _ = Task.Run(async () =>
            {
                await Task.Delay(60);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Shell.SetTabBarIsVisible(this, true);
                });
            });

            await slideTask;
            ChatOverlay.InputTransparent = true;
            ChatOverlay.TranslationY = 3000;
        }
        catch (InvalidOperationException)
        {
            ChatOverlay.InputTransparent = true;
            ChatOverlay.TranslationY = 3000;
            Shell.SetTabBarIsVisible(this, true);
        }
        finally
        {
            _animating = false;
        }
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
            var offscreenY = Height > 0 ? Height : GetFallbackHeight();
            SearchOverlay.TranslationY = offscreenY;
            SearchOverlay.InputTransparent = false;
            Shell.SetTabBarIsVisible(this, false);

            await SearchOverlay.TranslateToAsync(0, 0, 220, Easing.CubicOut);
            SearchOverlay.FocusSearchInput();
        }
        catch (InvalidOperationException)
        {
            SearchOverlay.TranslationY = 0;
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
            SearchOverlay.Reset();
            var offscreenY = Height > 0 ? Height : GetFallbackHeight();
            var slideTask = SearchOverlay.TranslateToAsync(0, offscreenY, 220, Easing.CubicIn);

            _ = Task.Run(async () =>
            {
                await Task.Delay(60);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Shell.SetTabBarIsVisible(this, true);
                });
            });

            await slideTask;
            SearchOverlay.InputTransparent = true;
            SearchOverlay.TranslationY = 3000;
        }
        catch (InvalidOperationException)
        {
            SearchOverlay.InputTransparent = true;
            SearchOverlay.TranslationY = 3000;
            Shell.SetTabBarIsVisible(this, true);
        }
        finally
        {
            _animating = false;
        }
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

    protected override bool OnBackButtonPressed()
    {
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

        if (Vm?.IsSelectionMode == true)
        {
            Vm.ExitSelectionMode();
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
