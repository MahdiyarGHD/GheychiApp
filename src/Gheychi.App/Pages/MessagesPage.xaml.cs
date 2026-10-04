using Gheychi.App.ViewModels;

namespace Gheychi.App.Pages;

public partial class MessagesPage : ContentPage
{
    private bool _animating;
    private long _lastScrollTime;
    private int _lastFirstVisible = -1;
    private int _lastLastVisible = -1;
    private bool _timerRunning;
    private MessagesViewModel? Vm => BindingContext as MessagesViewModel;

    public MessagesPage(MessagesViewModel? vm = null)
    {
        InitializeComponent();
        BindingContext = vm ?? IPlatformApplication.Current?.Services.GetService<MessagesViewModel>() ?? new MessagesViewModel();
        ChatOverlay.BackRequested += CloseChatAsync;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Vm != null)
            await Vm.InitializeAsync();
    }

    private void OnThreadsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _lastFirstVisible = e.FirstVisibleItemIndex;
        _lastLastVisible = e.LastVisibleItemIndex;
        _lastScrollTime = Environment.TickCount64;

        if (!_timerRunning)
        {
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

    private async void OnThreadTapped(object? sender, SelectionChangedEventArgs e)
    {
        var list = (CollectionView)sender!;
        // While a chat is open (or animating), any tap reaching the thread list
        // is a leak through the overlay — drop it so a second chat can never open.
        if (_animating || !ChatOverlay.InputTransparent)
        {
            if (list.SelectedItem != null)
                list.SelectedItem = null;
            return;
        }
        if (e.CurrentSelection.FirstOrDefault() is ThreadItem thread)
        {
            list.SelectedItem = null;
            var wasUnread = thread.IsUnread;
            if (wasUnread)
            {
                thread.MarkAsRead();
            }
            await OpenChatAsync(thread, wasUnread);
        }
    }

    private async Task OpenChatAsync(ThreadItem thread, bool wasUnread = false)
    {
        if (_animating)
            return;
        _animating = true;
        try
        {
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

    protected override bool OnBackButtonPressed()
    {
        if (!ChatOverlay.InputTransparent)
        {
            if (ChatOverlay.HandleBack())
                return true;

            MainThread.BeginInvokeOnMainThread(async () => await CloseChatAsync());
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
