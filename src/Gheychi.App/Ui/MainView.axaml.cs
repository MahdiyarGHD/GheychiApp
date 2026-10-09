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

namespace Gheychi.App.Ui;

/// <summary>
/// The whole window: the three tabs, a layer above them for the full-screen overlays (chat, search, compose...) and the
/// soft keyboard and system bars. Overlays cover the tab bar by sitting above it, so opening one never relayouts the page
/// beneath, and the pages stay built across tab switches.
/// </summary>
public partial class MainView : UserControl
{
    private const int MessagesIndex = 0;
    private const int SpamIndex = 1;
    private const int SettingsIndex = 2;

    private static readonly IBrush TabActive = Palette.Pick("#1B5E43", "#6FD3A8");
    private static readonly IBrush TabInactive = Palette.Pick("#595C61", "#9AA0AB");

    private readonly Control?[] _pages = new Control?[3];
    private int _selected = -1;
    private TopLevel? _topLevel;
    private double _safeBottom;
    private bool _detached;

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
        Select(ChatLaunchRequests.HasPending ? MessagesIndex : SpamTabRequests.Take() ? SpamIndex : MessagesIndex);

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
        if (_topLevel.InsetsManager is { } insets)
        {
            // Edge to edge from Android 11: the window is then never resized for the keyboard, which redrew the whole
            // surface (a white flash) each time it opened. The keyboard's room is made by ApplyInsets instead.
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
                insets.DisplayEdgeToEdgePreference = true;

            // Below Android 15 the bars have a colour of their own, which would show against the other theme.
            insets.SystemBarColor = Color.Parse(ThemeState.IsDark ? "#121212" : "#F1F3F4");
            insets.SafeAreaChanged += OnInsetsChanged;
        }
        if (_topLevel.InputPane is { } pane)
            pane.StateChanged += OnInsetsChanged;
        if (ContentView is { } content)
            content.LayoutChange += OnContentLayoutChanged;
        ApplyInsets();
        // After the app restarts itself the first insets are read before the window has settled, and nothing reports
        // the final ones (only the keyboard opening did, which is why a search fixed it).
        DispatcherTimer.RunOnce(ApplyInsets, TimeSpan.FromMilliseconds(250));
        DispatcherTimer.RunOnce(ApplyInsets, TimeSpan.FromMilliseconds(1000));

        ChatLaunchRequests.Requested += OnChatLaunchRequested;
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

    private static void OnAppResumed() => _ = Services.UpdateAutoCheck.RunAsync();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        ChatLaunchRequests.Requested -= OnChatLaunchRequested;
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
            if (_topLevel.InsetsManager is { } insets)
                insets.SafeAreaChanged -= OnInsetsChanged;
            if (_topLevel.InputPane is { } pane)
                pane.StateChanged -= OnInsetsChanged;
            _topLevel = null;
        }
    }

    public void SetTabBarVisible(bool visible) => TabBar.IsVisible = visible;

    public void SelectMessagesTab() => Select(MessagesIndex);

    // ---- Tabs ----------------------------------------------------------------------------------

    private void OnTabTapped(object? sender, TappedEventArgs e)
    {
        if (sender == MessagesTab)
            Select(MessagesIndex);
        else if (sender == SpamTab)
            Select(SpamIndex);
        else if (sender == SettingsTab)
            Select(SettingsIndex);
    }

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

        Style(MessagesPill, MessagesIcon, MessagesLabel, index == MessagesIndex);
        Style(SpamPill, SpamIcon, SpamLabel, index == SpamIndex);
        Style(SettingsPill, SettingsIcon, SettingsLabel, index == SettingsIndex);

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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Building the other tabs failed: {ex}");
        }
    }

    private static void Style(Border pill, Icon icon, TextBlock label, bool selected)
    {
        pill.IsVisible = selected;
        var brush = selected ? TabActive : TabInactive;
        icon.Foreground = brush;
        label.Foreground = brush;
        label.FontFamily = selected ? AppFonts.Bold : AppFonts.Regular;
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
        if (_selected < 0)
            return;

        var handled = _pages[_selected] switch
        {
            MessagesPage messages => messages.HandleBack(),
            SpamPage spam => spam.HandleBack(),
            SettingsPage settings => settings.HandleBack(),
            _ => false
        };
        if (handled)
            e.Handled = true;
    }

    // ---- Keyboard and system bars --------------------------------------------------------------

    private void OnInsetsChanged(object? sender, EventArgs e) => ApplyInsets();

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
            keyboard = Math.Max(0, _topLevel.Bounds.Height - pane.OccludedRect.Top);

        // The keyboard covers the navigation bar, so while it is up it alone sets the bottom edge.
        _safeBottom = keyboard > 0 ? 0 : safe.Bottom;
        Root.Padding = new Thickness(safe.Left, safe.Top, safe.Right, keyboard);
        TabBar.Padding = new Thickness(0, 0, 0, _safeBottom);
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

            MainThread.BeginInvokeOnMainThread(() => SettingsBadge.IsVisible = app || model);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings tab badge failed: {ex}");
        }
    }

    // ---- Horizontal page swipe -----------------------------------------------------------------
    // Every touch passes here before the views do; a horizontal drag is handed to the active client, and the pointer is
    // captured so the list underneath drops the touch and both never react to the same drag.

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        UserActivity.Touched();
        EndSwipeTracking();
        if (_swipe is null || PageSwipe.Client is not { } client)
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
