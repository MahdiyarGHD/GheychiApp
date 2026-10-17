using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Gheychi.App.Gestures;
using Gheychi.App.Localization;
using Gheychi.App.Pages;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;
using Gheychi.Core.Updates;
using Gheychi.App.Theming;

namespace Gheychi.App.Ui;

/// <summary>
/// The whole window: the inbox, the Spam page and the Settings page (the last two are opened from the inbox's header), a
/// layer above them for the full-screen overlays (chat, search, compose...) and the soft keyboard and system bars. The
/// pages stay built across switches, and opening an overlay never relayouts the page beneath.
/// </summary>
public partial class MainView : UserControl
{
    private const int MessagesIndex = 0;
    private const int SpamIndex = 1;
    private const int SettingsIndex = 2;

    private readonly Control?[] _pages = new Control?[3];
    private int _selected = -1;
    private TopLevel? _topLevel;
    private double _safeBottom;
    private bool _detached;
    private bool _settingsBadge;

    private HorizontalSwipeTracker? _swipe;
    private IPageSwipeClient? _swipeClient;
    private readonly Queue<(long Tick, double X)> _samples = new();

    public static MainView? Current { get; private set; }

    /// <summary>The layer full-screen overlays are added to; later children are above earlier ones (or use <c>ZIndex</c>).</summary>
    public Panel Overlays => OverlayHost;

    public MainView()
    {
        InitializeComponent();
        Current = this;
        // Insets are applied by ApplyInsets, which lets Root paint under the system bars.
        TopLevel.SetAutoSafeAreaPadding(this, false);
        FontFamily = AppFonts.Regular;
        FlowDirection = CultureService.GetFlowDirection();
        DispatcherTimer.RunOnce(MainActivity.UseAppBackground, TimeSpan.FromMilliseconds(800));

        // A tapped notification opens a chat or the spam tab; the request is kept until this view is up.
        Select(ChatLaunchRequests.HasPending || ExternalSendRequests.HasPending ? MessagesIndex : SpamTabRequests.Take() ? SpamIndex : MessagesIndex);

        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is null)
            return;

        var slop = (Platform.AppContext.Resources?.DisplayMetrics?.Density ?? 1) is var density and > 0
            ? (global::Android.Views.ViewConfiguration.Get(Platform.AppContext)?.ScaledTouchSlop ?? 24) / density
            : 8;
        _swipe = new HorizontalSwipeTracker(slop);

        _topLevel.BackRequested += OnBackRequested;
        // Until Avalonia's surface exists its scaling is a placeholder 1, and the bars' insets (pixels) were taken as
        // that many DIPs: a launch could keep a band of several times the bar height above and below the app.
        // The insets are read again once the real scaling is known, which happens before the first frame.
        _topLevel.ScalingChanged += OnScalingChanged;
        if (_topLevel.InsetsManager is { } insets)
        {
            // Edge to edge from Android 11: the window is then never resized for the keyboard, which redrew the whole
            // surface (a white flash) each time it opened. The keyboard's room is made by ApplyInsets instead.
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
                insets.DisplayEdgeToEdgePreference = true;

            // Below Android 15 the bars have a colour of their own, which would show against the other theme.
            insets.SystemBarColor = Color.Parse(ThemeState.IsDark ? "#121212" : "#F1F3F4");
            ColorSystemBars();
            insets.SafeAreaChanged += OnInsetsChanged;
        }
        if (_topLevel.InputPane is { } pane)
            pane.StateChanged += OnInsetsChanged;
        if (ContentView is { } content)
            content.LayoutChange += OnContentLayoutChanged;
        ApplyInsets();

        ChatLaunchRequests.Requested += OnChatLaunchRequested;
        ExternalSendRequests.Requested += OnChatLaunchRequested;
        SpamTabRequests.Requested += OnSpamTabRequested;
        MainActivity.Resumed += OnAppResumed;
        SubscribeUpdates();
        OnAppResumed();

        if (_detached)
        {
            _detached = false;
            if (_selected >= 0 && _pages[_selected] is { } shown)
                NotifyShown(shown);
        }
    }

    // The splash theme paints the bars in the phone's light or dark page colour; both take the page's.
    private static void ColorSystemBars()
    {
        if (Platform.CurrentActivity?.Window is not { } window)
            return;

        window.SetStatusBarColor(global::Android.Graphics.Color.ParseColor(ThemeState.IsDark ? "#121212" : "#F1F3F4"));
        window.SetNavigationBarColor(global::Android.Graphics.Color.ParseColor(ThemeState.IsDark ? "#121212" : "#F1F3F4"));
    }

    private void OnAppResumed()
    {
        _ = Services.UpdateAutoCheck.RunAsync();
        // The keyboard and the bars may have changed while the app was away without a callback reaching it.
        Dispatcher.UIThread.Post(ApplyInsets, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        ChatLaunchRequests.Requested -= OnChatLaunchRequested;
        ExternalSendRequests.Requested -= OnChatLaunchRequested;
        SpamTabRequests.Requested -= OnSpamTabRequested;
        MainActivity.Resumed -= OnAppResumed;
        // The pages subscribe to static events while shown; an activity that is gone must not keep answering them.
        _detached = true;
        foreach (var page in _pages)
        {
            if (page is not null)
                NotifyHidden(page);
        }

        if (ContentView is { } content)
            content.LayoutChange -= OnContentLayoutChanged;
        if (_topLevel is not null)
        {
            _topLevel.BackRequested -= OnBackRequested;
            _topLevel.ScalingChanged -= OnScalingChanged;
            if (_topLevel.InsetsManager is { } insets)
                insets.SafeAreaChanged -= OnInsetsChanged;
            if (_topLevel.InputPane is { } pane)
                pane.StateChanged -= OnInsetsChanged;
            _topLevel = null;
        }
    }

    public void SelectMessagesTab() => Select(MessagesIndex);

    public void ShowSpam() => Select(SpamIndex);

    public void ShowSettings() => Select(SettingsIndex);

    private void OnChatLaunchRequested() => MainThread.BeginInvokeOnMainThread(() => Select(MessagesIndex));

    private void OnSpamTabRequested() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (SpamTabRequests.Take())
            Select(SpamIndex);
    });

    private void Select(int index)
    {
        if (index == _selected)
            return;

        var previous = _selected;
        _selected = index;

        if (previous >= 0 && _pages[previous] is { } old)
        {
            old.IsVisible = false;
            NotifyHidden(old);
        }

        var page = _pages[index] ??= CreatePage(index);
        page.IsVisible = true;
        NotifyShown(page);

        if (previous < 0)
            _ = BuildOtherTabsAsync();
    }

    private Control CreatePage(int index)
    {
        Control page = index switch
        {
            SpamIndex => CreateSpamPage(),
            SettingsIndex => new SettingsPage(),
            _ => new MessagesPage()
        };
        page.IsVisible = false;
        if (page is MessagesPage messages)
            messages.SetSettingsBadge(_settingsBadge);
        PageHost.Children.Add(page);
        return page;
    }

    private SpamPage CreateSpamPage()
    {
        var spam = new SpamPage();
        spam.OpenSpamSettingsRequested += () =>
        {
            Select(SettingsIndex);
            (_pages[SettingsIndex] as SettingsPage)?.OpenSpamSettings();
        };
        return spam;
    }

    // Switching tabs only toggles visibility, so the first switch must not pay for building the page: the other tabs
    // are built once the inbox has drawn and is left alone.
    private async Task BuildOtherTabsAsync()
    {
        try
        {
            await Task.Delay(600);
            foreach (var index in (int[])[SpamIndex, SettingsIndex])
            {
                await UserActivity.WaitForIdleAsync();
                _pages[index] ??= CreatePage(index);
                await Task.Delay(300);
            }

            if (_pages[SettingsIndex] is SettingsPage settings)
                await settings.WarmScreensAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Building the other tabs failed: {ex}");
        }
    }

    private static void NotifyShown(Control page)
    {
        switch (page)
        {
            case MessagesPage messages:
                messages.OnShown();
                break;
            case SpamPage spam:
                spam.OnShown();
                break;
            case SettingsPage settings:
                settings.OnShown();
                break;
        }
    }

    private static void NotifyHidden(Control page)
    {
        switch (page)
        {
            case MessagesPage messages:
                messages.OnHidden();
                break;
            case SpamPage spam:
                spam.OnHidden();
                break;
            case SettingsPage settings:
                settings.OnHidden();
                break;
        }
    }

    // ---- Back ----------------------------------------------------------------------------------

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (Sheet.HandleBack())
        {
            e.Handled = true;
            return;
        }

        if (_selected < 0)
            return;

        var handled = _pages[_selected] switch
        {
            MessagesPage messages => messages.HandleBack(),
            SpamPage spam => spam.HandleBack(),
            SettingsPage settings => settings.HandleBack(),
            _ => false
        };

        // Back from the Spam or Settings page goes to the inbox, not out of the app.
        if (!handled && _selected != MessagesIndex)
        {
            Select(MessagesIndex);
            handled = true;
        }

        if (handled)
            e.Handled = true;
    }

    // ---- Keyboard and system bars --------------------------------------------------------------

    private void OnInsetsChanged(object? sender, EventArgs e) => ApplyInsets();

    private void OnScalingChanged(object? sender, EventArgs e) => ApplyInsets();

    private global::Android.Views.View? ContentView =>
        Platform.CurrentActivity?.FindViewById(global::Android.Resource.Id.Content);

    private Thickness ViewOffset()
    {
        if (_topLevel is null || ContentView is not { Width: > 0, Height: > 0 } content || content.RootView is not { } window)
            return default;

        var at = new int[2];
        content.GetLocationInWindow(at);
        var scale = _topLevel.RenderScaling;
        return new Thickness(
            at[0] / scale,
            at[1] / scale,
            (window.Width - at[0] - content.Width) / scale,
            (window.Height - at[1] - content.Height) / scale);
    }

    // Coming back to the app with the keyboard up, the pane can briefly report a rectangle that is not the keyboard (the
    // whole window, or one that starts at the top). Taken as the keyboard's room it left the page no height at all, with
    // only the tab bar showing at the top. The room is measured to the window's bottom edge, as the keyboard's rectangle
    // may stop short of it (the navigation bar); a keyboard never takes most of the window.
    private static double KeyboardHeight(Rect occluded, double windowHeight)
    {
        if (windowHeight <= 0 || occluded.Height <= 0)
            return 0;

        var height = windowHeight - occluded.Top;
        return height > 0 && height <= windowHeight * 0.6 ? height : 0;
    }

    private void OnContentLayoutChanged(object? sender, global::Android.Views.View.LayoutChangeEventArgs e) => ApplyInsets();

    private void ApplyInsets()
    {
        if (_topLevel is null)
            return;

        var safe = _topLevel.InsetsManager?.SafeAreaPadding ?? default;
        // Right after the app restarts itself the system can still be fitting the window around the bars while the
        // insets are reported as well; what the view is already inset by is not applied a second time.
        if (_topLevel.InsetsManager?.DisplaysEdgeToEdge == true)
        {
            var offset = ViewOffset();
            safe = new Thickness(
                Math.Max(0, safe.Left - offset.Left),
                Math.Max(0, safe.Top - offset.Top),
                Math.Max(0, safe.Right - offset.Right),
                Math.Max(0, safe.Bottom - offset.Bottom));
        }

        var keyboard = 0.0;
        // Where the window is not edge to edge (before Android 11) the system shrinks it for the keyboard itself.
        if (_topLevel.InsetsManager?.DisplaysEdgeToEdge == true && _topLevel.InputPane is { State: InputPaneState.Open } pane)
            keyboard = KeyboardHeight(pane.OccludedRect, _topLevel.Bounds.Height);

        // The keyboard covers the navigation bar, so while it is up it alone sets the bottom edge.
        _safeBottom = keyboard > 0 ? 0 : safe.Bottom;
        Root.Padding = new Thickness(safe.Left, safe.Top, safe.Right, keyboard);
        PageHost.Margin = new Thickness(0, 0, 0, _safeBottom);
        OverlayHost.Margin = new Thickness(0, 0, 0, _safeBottom);
    }

    // ---- Settings tab dot ----------------------------------------------------------------------

    private bool _subscribed;

    /// <summary>
    /// A dot on the Settings tab while a newer app version or spam model is waiting. The update itself is only offered
    /// inside Settings, which nobody opens to look for one.
    /// </summary>
    private void SubscribeUpdates()
    {
        if (_subscribed || IPlatformApplication.Current?.Services is not { } services)
            return;

        _subscribed = true;
        if (services.GetService<AppUpdates>() is { } app)
            app.Changed += (_, _) => _ = UpdateBadgeAsync();
        if (services.GetService<SpamModelUpdates>() is { } model)
            model.Changed += (_, _) => _ = UpdateBadgeAsync();
        _ = UpdateBadgeAsync();
    }

    private async Task UpdateBadgeAsync()
    {
        try
        {
            var services = IPlatformApplication.Current?.Services;
            var app = services?.GetService<AppUpdates>()?.GetAvailable() is not null;
            var model = !app && services?.GetService<SpamModelUpdates>() is { } models
                && await Task.Run(() => models.GetAvailableAsync()) is not null;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _settingsBadge = app || model;
                (_pages[MessagesIndex] as MessagesPage)?.SetSettingsBadge(_settingsBadge);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings badge failed: {ex}");
        }
    }

    // ---- Horizontal page swipe -----------------------------------------------------------------
    // Every touch passes here before the views do; a horizontal drag is handed to the active client, and the pointer is
    // captured so the list underneath drops the touch and both never react to the same drag.

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        UserActivity.Touched();
        EndSwipeTracking();
        if (Sheet.IsOpen || _swipe is null || PageSwipe.Client is not { } client || !client.CanSwipeFrom(e.Source as Visual))
            return;

        _swipeClient = client;
        var point = e.GetPosition(this);
        _swipe.Begin(point.X, point.Y);
        _samples.Clear();
        AddSample(point.X);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_swipe is null || _swipeClient is null)
            return;

        var point = e.GetPosition(this);
        AddSample(point.X);
        var wasDragging = _swipe.IsDragging;
        if (!_swipe.Move(point.X, point.Y, _swipeClient.AllowedSwipeSign))
            return;

        if (!wasDragging)
        {
            e.Pointer.Capture(this);
            _swipeClient.OnSwipeStarted();
        }

        _swipeClient.OnSwipeMoved(_swipe.Offset);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => FinishSwipe(cancelled: false, e.Pointer);

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Losing the capture to anything but this view while dragging is a cancel; releasing it ourselves is not.
        if (_swipe is { IsDragging: true } && !ReferenceEquals(e.Pointer.Captured, this))
            FinishSwipe(cancelled: true, e.Pointer);
    }

    private void FinishSwipe(bool cancelled, IPointer pointer)
    {
        if (_swipeClient is { } client && _swipe is { IsDragging: true })
        {
            var offset = _swipe.Offset;
            var velocity = CurrentVelocity();
            EndSwipeTracking();
            if (ReferenceEquals(pointer.Captured, this))
                pointer.Capture(null);
            client.OnSwipeEnded(offset, velocity, cancelled);
            return;
        }

        EndSwipeTracking();
    }

    private void AddSample(double x)
    {
        var now = Environment.TickCount64;
        _samples.Enqueue((now, x));
        while (_samples.Count > 1 && now - _samples.Peek().Tick > 100)
            _samples.Dequeue();
    }

    private double CurrentVelocity()
    {
        if (_samples.Count < 2)
            return 0;

        var first = _samples.Peek();
        var last = _samples.Last();
        var seconds = (last.Tick - first.Tick) / 1000.0;
        return seconds <= 0 ? 0 : (last.X - first.X) / seconds;
    }

    private void EndSwipeTracking()
    {
        _swipe?.Reset();
        _swipeClient = null;
        _samples.Clear();
    }
}
