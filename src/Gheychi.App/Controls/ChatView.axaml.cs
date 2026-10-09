using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gheychi.App.Localization;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;
using Gheychi.App.Theming;

namespace Gheychi.App.Controls;

/// <summary>Where a message bubble was on screen when it was held, in dips from the top of the chat.</summary>
public sealed record MessageBounds(double Y, double Height, double Width = 0);

public partial class ChatView : UserControl
{
    private const int OlderTriggerAhead = 40;

    private static readonly TimeSpan ButtonFade = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan OverlayOpen = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan OverlayClose = TimeSpan.FromMilliseconds(150);

    // True while the newest message is on screen; decides if an arriving message is followed or
    // only announced by the scroll-to-bottom button.
    private bool _followTail = true;
    private ChatViewModel? _liveVm;
    private bool _initialLayoutSettled;
    private bool _buttonVisible;
    private bool _loadingOlder;
    private bool _stickPending;
    private bool _stickyPending;
    private bool _holdFired;
    private bool _stoppedFling;
    private long _lastOffsetChangeTick;
    private int _lastFirstVisibleIndex = -1;
    private long _lastDateUpdateTime;
    private double _targetAnchorX;
    private Geometry? _starIcon;
    private Geometry? _starFillIcon;

    private readonly IBlockedSenders? _blocked;
    private ChatMessage? _targetMessage;

    internal ChatViewModel? Vm => DataContext as ChatViewModel;

    public ChatView()
    {
        InitializeComponent();

        var initialVm = new ChatViewModel();
        initialVm.SafeDispatcher = SafePrependItems;
        DataContext = initialVm;

        _blocked = IPlatformApplication.Current?.Services.GetService<IBlockedSenders>();
        if (_blocked is not null)
            _blocked.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshBlocked);

        SimDigit.RenderTransform = new TranslateTransform(0, AppFonts.BadgeDigitOffsetY + 1.4);
        TextInputOptions.SetReturnKeyType(SearchEntry, TextInputReturnKeyType.Search);
        // Without these the keyboard's enter key is a tick that closes it, instead of a new line.
        TextInputOptions.SetMultiline(MessageEntry, true);
        TextInputOptions.SetReturnKeyType(MessageEntry, TextInputReturnKeyType.Return);

        // One handler per gesture for the whole list: a row has no handlers of its own.
        MessagesList.Scrolled += OnMessagesScrolled;
        MessagesList.AddHandler(PointerPressedEvent, OnMessagePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        MessagesList.AddHandler(TappedEvent, OnMessageTapped);
        MessagesList.AddHandler(HoldingEvent, OnMessageHolding);

        // The soft keyboard resizes the page: the box being focused means the newest message has to stay in view.
        MessageEntry.GotFocus += (_, _) => ScrollToEnd();
    }

    public event Func<Task>? BackRequested;

    public bool IsChatOpen => true;

    public bool SuppressAutoScroll { get; set; }

    /// <summary>
    /// Resets the chat for <paramref name="thread"/> before it slides in and returns its cached first page, if any.
    /// Nothing is bound to the list here: filling it is the slow part of opening a chat, and doing it before or
    /// during the slide (which runs on the UI thread) held the slide back. The caller applies the page after.
    /// </summary>
    public PreparedChatData? PrepareForTransition(ThreadItem thread)
    {
        var cached = PrepareForTransitionCore(thread);
        AttachLiveUpdates();
        return cached;
    }

    private void AttachLiveUpdates()
    {
        if (Vm is null)
            return;

        if (!ReferenceEquals(_liveVm, Vm))
        {
            DetachLiveUpdates();
            _liveVm = Vm;
            _liveVm.MessagesAppended += OnMessagesAppended;
        }

        _liveVm.StartLiveUpdates();
    }

    private void DetachLiveUpdates()
    {
        if (_liveVm is null)
            return;

        _liveVm.MessagesAppended -= OnMessagesAppended;
        _liveVm.StopLiveUpdates();
        _liveVm = null;
    }

    private void OnMessagesAppended()
    {
        if (_followTail)
            ScrollToEnd();
        else
            ShowScrollToBottomButton();
    }

    private PreparedChatData? PrepareForTransitionCore(ThreadItem thread)
    {
        _followTail = true;
        _initialLayoutSettled = false;
        _lastFirstVisibleIndex = -1;
        _loadingOlder = false;
        HideScrollToBottomButton();

        if (Vm is not { } vm)
        {
            vm = new ChatViewModel();
            DataContext = vm;
        }

        vm.ResetForOpen();
        vm.Thread = thread;
        RefreshBlocked();
        vm.SafeDispatcher = SafePrependItems;
        vm.Items.Clear();
        vm.Messages.Clear();
        vm.StickyDate = string.Empty;
        ShowSkeleton();

        return ChatViewModel.TryGetCached(thread.ThreadId, out var cached) ? cached : null;
    }

    /// <summary>Reads the first page off the UI thread; call from the UI thread.</summary>
    public Task<PreparedChatData?> FetchMessagesAsync(ThreadItem thread, CancellationToken ct = default, int unreadHint = 0)
    {
        if (Vm is not { } vm)
            return Task.FromResult<PreparedChatData?>(null);

        return Task.Run<PreparedChatData?>(async () =>
            await vm.FetchMessagesAsync(thread, cancellationToken: ct, unreadHint: unreadHint));
    }

    public void ApplyMessages(PreparedChatData data)
    {
        HideSkeleton();
        if (Vm is null)
            return;

        Vm.SafeDispatcher = SafePrependItems;
        Vm.ApplyMessages(data, data.RawCount);

        ScrollToInitialPosition(data.FirstUnreadIndex);
    }

    public void Bind(ThreadItem thread)
    {
        if (PrepareForTransition(thread) is { } cached)
        {
            ApplyMessages(cached);
            return;
        }

        if (Vm is not null)
        {
            _ = Vm.LoadMessagesAsync(thread).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    HideSkeleton();
                    ScrollToBottom();
                });
            });
        }
    }

    public void Bind(ChatViewModel vm)
    {
        _initialLayoutSettled = false;
        _lastFirstVisibleIndex = -1;
        vm.SafeDispatcher = SafePrependItems;
        HideSkeleton();
        HideScrollToBottomButton();
        DataContext = vm;
        RefreshBlocked();
    }

    private void RefreshBlocked()
    {
        var blocked = _blocked is not null && Vm?.Thread.Phone is { Length: > 0 } phone && _blocked.IsBlocked(phone);
        BlockedBar.IsVisible = blocked;
        Composer.IsVisible = !blocked;
    }

    // The whole pill is the input, not only the line of text in it.
    private void OnInputBarTapped(object? sender, TappedEventArgs e) => MessageEntry.Focus();

    private void OnUnblockTapped(object? sender, TappedEventArgs e)
    {
        if (_blocked is null || Vm?.Thread.Phone is not { Length: > 0 } phone)
            return;

        _blocked.SetBlocked(phone, false);
        Toast.Show(LocalizationManager.Instance["Profile_Unblocked"]);
        RefreshBlocked();
    }

    public void ScrollToBottom() => ScrollToEnd();

    public Task Open(ChatViewModel vm)
    {
        Bind(vm);
        return Task.CompletedTask;
    }

    public Task Close()
    {
        HeaderMenuOverlay.IsVisible = false;
        CloseSearch();
        DetachLiveUpdates();
        HideSkeleton();
        DropSelection(MessageEntry);
        DropSelection(SearchEntry);
        ReleaseInputFocus();
        return Task.CompletedTask;
    }

    // The selection handles live in a layer above the page and are only removed when the box loses focus with its
    // menu closed; a box that lost focus while its menu was open would leave them on screen after the chat is gone.
    private void DropSelection(TextBox box)
    {
        var menuOpen = TextMenu.IsOpen(box);
        TextMenu.Close(box);
        if (!menuOpen && box.SelectionStart == box.SelectionEnd)
            return;

        box.ClearSelection();
        if (!box.IsFocused)
            box.Focus();
        PageContent.Focus();
    }

    /// <summary>Puts the cursor in the message box (and brings up the keyboard).</summary>
    public void FocusMessageInput() => MessageEntry.Focus();

    /// <summary>Takes the keyboard down, e.g. before another page covers the chat.</summary>
    public void ReleaseInputFocus()
    {
        if (MessageEntry.IsFocused || SearchEntry.IsFocused)
            PageContent.Focus();
    }

    /// <summary>Called once the overlay is out of sight: no popup of this chat may still be showing when it opens again.</summary>
    public void ResetAfterClose()
    {
        SelectionOverlay.IsVisible = false;
        MessageInfoModal.IsVisible = false;
        HeaderMenuOverlay.IsVisible = false;
    }

    // ---- Scrolling ----------------------------------------------------------------------------

    /// <summary>Opens at the first unread message when there is one, otherwise at the newest.</summary>
    public void ScrollToInitialPosition(int? firstUnreadIndex)
    {
        if (Vm is null || Vm.Items.Count == 0)
        {
            _initialLayoutSettled = true;
            HideScrollToBottomButton();
            return;
        }

        var count = Vm.Items.Count;
        var isUnread = firstUnreadIndex is >= 0 && firstUnreadIndex.Value < count;
        if (isUnread)
        {
            _followTail = false;
            MessagesList.ScrollToIndex(firstUnreadIndex!.Value, RowAlignment.Start);
        }
        else
        {
            ScrollToEnd();
        }

        if (!isUnread || firstUnreadIndex!.Value >= count - 2)
            HideScrollToBottomButton();

        // Anchoring at the target has to be done before history pagination is allowed: it moves the first row.
        Dispatcher.UIThread.Post(() =>
        {
            _initialLayoutSettled = true;
            UpdateScrollButtonVisibility();
        }, DispatcherPriority.Background);
    }

    private void ScrollToEnd()
    {
        if (SuppressAutoScroll || Vm is null || Vm.Items.Count == 0)
            return;

        MessagesList.ScrollToEnd();
        _followTail = true;
        HideScrollToBottomButton();
    }

    /// <summary>
    /// Runs a change that prepends rows (a history page) and keeps the reader's place: the panel keeps its offset
    /// when rows are inserted above it, which would jump the list, so the rows on screen are put back afterwards.
    /// </summary>
    public void SafePrependItems(Action action)
    {
        var anchors = MessagesList.CaptureAnchors();
        action();
        if (anchors.Length > 0)
            MessagesList.RestoreAnchor(anchors);

        _lastFirstVisibleIndex = -1;
    }

    private void OnScrollToBottomTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is null || Vm.Items.Count == 0)
            return;

        HideScrollToBottomButton();
        ScrollToEnd();
    }

    private void UpdateScrollButtonVisibility()
    {
        if (Vm is null || Vm.Items.Count == 0 || MessagesList.IsAtBottom)
            HideScrollToBottomButton();
        else
            ShowScrollToBottomButton();
    }

    private void ShowScrollToBottomButton()
    {
        if (_buttonVisible)
            return;

        _buttonVisible = true;
        ScrollToBottomButton.IsVisible = true;
        _ = OverlayAnimator.FadeAsync(ScrollToBottomButton, 0, 1, ButtonFade);
    }

    private void HideScrollToBottomButton()
    {
        if (!_buttonVisible)
            return;

        _buttonVisible = false;
        _ = HideScrollToBottomButtonAsync();
    }

    private async Task HideScrollToBottomButtonAsync()
    {
        await OverlayAnimator.FadeAsync(ScrollToBottomButton, 1, 0, ButtonFade);
        if (_buttonVisible)
            ScrollToBottomButton.Opacity = 1;
        else
            ScrollToBottomButton.IsVisible = false;
    }

    private void OnMessagesScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y != 0)
            _lastOffsetChangeTick = Environment.TickCount64;

        if (Vm is not { } vm || vm.Items.Count == 0)
            return;

        // The page got shorter or taller (the keyboard): stay on the newest message.
        if (e.ViewportDelta.Y != 0 && _followTail)
            StickToBottomSoon();

        var first = MessagesList.FirstVisibleIndex;
        var last = MessagesList.LastVisibleIndex;
        if (last >= vm.Items.Count - 2 || MessagesList.IsAtBottom)
        {
            _followTail = true;
            HideScrollToBottomButton();
        }
        else if (last >= 0)
        {
            _followTail = false;
            ShowScrollToBottomButton();
        }

        if (first < 0 || first == _lastFirstVisibleIndex)
            return;

        var oldFirst = _lastFirstVisibleIndex;
        _lastFirstVisibleIndex = first;

        // Throttle sticky date updates to at most once per 180ms during fast scrolling; the last position still gets its label.
        var now = Environment.TickCount64;
        if (now - _lastDateUpdateTime > 180)
        {
            _lastDateUpdateTime = now;
            UpdateStickyDate();
        }
        else if (!_stickyPending)
        {
            _stickyPending = true;
            DispatcherTimer.RunOnce(() =>
            {
                _stickyPending = false;
                _lastDateUpdateTime = Environment.TickCount64;
                UpdateStickyDate();
            }, TimeSpan.FromMilliseconds(180));
        }

        // Never on open: the first layout moves the first row. Only while the reader moves up toward older rows.
        if (_initialLayoutSettled && oldFirst >= 0 && first < oldFirst && first <= OlderTriggerAhead && vm.HasMore)
            TriggerLoadOlder();
    }

    private void UpdateStickyDate()
    {
        if (Vm is not { } vm || _lastFirstVisibleIndex < 0)
            return;

        var day = vm.GetDateForIndex(_lastFirstVisibleIndex);
        if (!string.IsNullOrEmpty(day) && day != vm.StickyDate)
            vm.StickyDate = day;
    }

    // Layout is still running when the scroll event arrives: scrolling right then would re-enter it.
    private void StickToBottomSoon()
    {
        if (_stickPending)
            return;

        _stickPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _stickPending = false;
            if (_followTail && !IsSearching)
                ScrollToEnd();
        });
    }

    private void TriggerLoadOlder()
    {
        if (Vm is not { } vm || _loadingOlder || !_initialLayoutSettled)
            return;

        _loadingOlder = true;

        // Posted, never started from the scroll event: the load may complete at once and change the list mid-layout.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var loaded = false;
            try
            {
                loaded = await vm.LoadOlderAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Loading older messages failed: {ex}");
            }
            finally
            {
                _loadingOlder = false;
            }

            // A fling that outran the page ended at the top, where no scroll event follows to ask for the next one.
            if (loaded && _initialLayoutSettled && vm.HasMore && ReferenceEquals(vm, Vm) && MessagesList.FirstVisibleIndex is >= 0 and <= OlderTriggerAhead)
                TriggerLoadOlder();
        });
    }

    // ---- Skeleton -----------------------------------------------------------------------------

    public void ShowSkeleton()
    {
        SkeletonOverlay.IsVisible = true;
        SkeletonOverlay.Opacity = 1;
        StartSkeletonPulse();
    }

    public void HideSkeleton()
    {
        StopSkeletonPulse();
        SkeletonOverlay.IsVisible = false;
    }

    // A compositor animation: nothing runs in managed code per frame while the chat opens.
    private void StartSkeletonPulse()
    {
        if (ElementComposition.GetElementVisual(SkeletonOverlay) is not { } visual)
            return;

        var easing = new SineEaseInOut();
        var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
        pulse.Target = "Opacity";
        pulse.InsertKeyFrame(0f, 1f, easing);
        pulse.InsertKeyFrame(1f, 0.35f, easing);
        pulse.Duration = TimeSpan.FromMilliseconds(550);
        pulse.Direction = PlaybackDirection.Alternate;
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", pulse);
    }

    private void StopSkeletonPulse()
    {
        if (ElementComposition.GetElementVisual(SkeletonOverlay) is not { } visual)
            return;

        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
    }

    // ---- Gestures on the list -----------------------------------------------------------------

    // A finger that lands on a list that is still moving stops it: that touch is not a tap or a hold on the row under it.
    private void OnMessagePressed(object? sender, PointerPressedEventArgs e)
    {
        _holdFired = false;
        _stoppedFling = Environment.TickCount64 - _lastOffsetChangeTick < 80;
    }

    private void OnMessageTapped(object? sender, TappedEventArgs e)
    {
        if (_holdFired || _stoppedFling || Vm is not { } vm || (e.Source as StyledElement)?.DataContext is not ChatMessage message)
            return;

        switch (PartOf(e.Source as Visual))
        {
            case "failed":
                vm.Retry(message);
                break;

            case "reaction":
                if (message.HasReactionFailed)
                    RetryReaction(vm, message);
                break;

            case "bubble":
                if (vm.IsSelectionMode)
                    vm.ToggleMessageSelection(message);
                else if (message.HasLink && LinkAt(e, message) is { } link)
                    OpenLink(message, link);
                else if (!message.IsOutgoing)
                    vm.ToggleSimTag(message);
                break;

            default:
                if (vm.IsSelectionMode)
                    vm.ToggleMessageSelection(message);
                break;
        }
    }

    private void OnMessageHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || _stoppedFling || Vm is not { IsSelectionMode: false })
            return;

        for (var visual = e.Source as Visual; visual is not null && !ReferenceEquals(visual, MessagesList); visual = visual.GetVisualParent())
        {
            if (visual is not Control { Tag: "bubble" } bubble)
                continue;

            if (bubble.DataContext is not ChatMessage message)
                return;

            // The finger is still down: its release must not count as a tap.
            _holdFired = true;
            e.Handled = true;
            TriggerHaptic(global::Android.Views.FeedbackConstants.LongPress);

            var origin = bubble.TranslatePoint(default, this) ?? default;
            OpenSelectionMenu(message, new MessageBounds(origin.Y, bubble.Bounds.Height, bubble.Bounds.Width));
            return;
        }
    }

    // The part of the row that was touched: the nearest ancestor that carries a marker in its Tag.
    private string? PartOf(Visual? source)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, MessagesList); visual = visual.GetVisualParent())
        {
            if (visual is Control { Tag: string tag })
                return tag;
        }

        return null;
    }

    // The link under the finger, if any: the text of the bubble is one block, so its layout says which character was hit.
    private LinkSpan? LinkAt(TappedEventArgs e, ChatMessage message)
    {
        for (var visual = e.Source as Visual; visual is not null && !ReferenceEquals(visual, MessagesList); visual = visual.GetVisualParent())
        {
            if (visual is TextBlock block && block.DataContext == message)
                return RichText.LinkAt(block, e.GetPosition(block), message);
        }

        return null;
    }

    private async void OpenLink(ChatMessage message, LinkSpan span)
    {
        try
        {
            var target = TextLinker.Target(message.Body, span);
            if (span.Kind == LinkKind.Url)
            {
                TriggerHaptic();
                if (!await Launcher.Default.OpenAsync(target))
                    await Dialogs.AlertAsync(string.Empty, LocalizationManager.Instance["Chat_LinkOpenFailed"], "OK");
                return;
            }

            var loc = LocalizationManager.Instance;
            var shown = message.Body.Substring(span.Start, span.Length);
            var choice = await Dialogs.ActionSheetAsync(shown, loc["Chat_Cancel"], null, loc["Chat_Call"], loc["Chat_CopyNumber"]);
            if (choice == loc["Chat_Call"])
                Platforms.Android.ProfileLauncher.Dial(target);
            else if (choice == loc["Chat_CopyNumber"])
                await Clipboard.Default.SetTextAsync(target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening a link failed: {ex}");
        }
    }

    private async void RetryReaction(ChatViewModel vm, ChatMessage message)
    {
        TriggerHaptic();
        await vm.RetryReactionAsync(message);
    }

    // ---- Header, send, SIM --------------------------------------------------------------------

    private async void OnBack(object? sender, TappedEventArgs e)
    {
        if (BackRequested is not null)
            await BackRequested.Invoke();
    }

    private void OnSend(object? sender, TappedEventArgs e)
    {
        if (Vm is null)
            return;

        var count = Vm.Items.Count;
        Vm.Send();
        if (Vm.Items.Count > count)
            ScrollToEnd();
    }

    private async void OnSimTap(object? sender, TappedEventArgs e)
    {
        if (Vm is null)
            return;

        var loc = LocalizationManager.Instance;
        var sims = Vm.ActiveSims;
        if (sims.Count <= 1)
            return;

        var options = new string[sims.Count];
        for (var i = 0; i < sims.Count; i++)
        {
            var sim = sims[i];
            options[i] = $"{sim.SlotIndex}. {sim.DisplayName}";
        }

        var choice = await Dialogs.ActionSheetAsync(loc["Chat_SimTitle"], loc["Chat_Cancel"], null, options);

        if (string.IsNullOrWhiteSpace(choice) || choice == loc["Chat_Cancel"])
            return;

        for (var i = 0; i < sims.Count; i++)
        {
            if (choice.StartsWith($"{sims[i].SlotIndex}.", StringComparison.Ordinal))
            {
                Vm.SelectSim(sims[i].SlotIndex, sims[i].SubId);
                break;
            }
        }
    }

    // A tap inside a popup belongs to the popup: it must not reach the scrim or the page behind it.
    private void OnMenuTapEater(object? sender, TappedEventArgs e) => e.Handled = true;

    // ---- Message popup (hold) -----------------------------------------------------------------

    public void OpenSelectionMenu(ChatMessage msg, MessageBounds? bounds = null)
    {
        if (msg is null)
            return;

        if (Vm?.IsSelectionMode == true)
        {
            Vm.ToggleMessageSelection(msg);
            return;
        }

        _targetMessage = msg;

        ElevatedBubbleBody.Text = msg.Body;
        ElevatedBubbleTime.Text = msg.Time;

        var isDark = ThemeState.IsDark;
        var side = msg.IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _targetAnchorX = msg.IsOutgoing ? 1.0 : 0.0;
        if (msg.IsOutgoing)
        {
            ElevatedBubbleBorder.Background = Palette.Accent(AccentRole.Solid);
            ElevatedBubbleBody.Foreground = Brushes.White;
        }
        else
        {
            ElevatedBubbleBorder.Background = ChatMessage.IncomingBubbleBg;
            ElevatedBubbleBody.Foreground = isDark ? Palette.Brush("#E8EAED") : Palette.Brush("#1B1E24");
        }

        ElevatedBubbleBorder.HorizontalAlignment = side;
        ElevatedMetadataGroup.HorizontalAlignment = side;
        ReactionDock.HorizontalAlignment = side;
        ContextActionMenu.HorizontalAlignment = side;

        CopyUrlActionRow.IsVisible = msg.HasUrl;
        MenuReportRow.IsVisible = !msg.IsOutgoing;

        var loc = LocalizationManager.Instance;
        MenuStarLabel.Text = msg.IsStarred ? loc["Chat_Unstar"] : loc["Chat_Star"];
        MenuStarIcon.Data = msg.IsStarred
            ? _starFillIcon ??= this.FindResource("Icon.StarFill") as Geometry
            : _starIcon ??= this.FindResource("Icon.Star") as Geometry;

        var screenH = Bounds.Height > 0 ? Bounds.Height : 800.0;
        var bubbleY = bounds?.Y ?? (screenH * 0.4);

        if (bubbleY > screenH * 0.52)
            OverlayContent.VerticalAlignment = VerticalAlignment.Bottom;
        else if (bubbleY < screenH * 0.28)
            OverlayContent.VerticalAlignment = VerticalAlignment.Top;
        else
            OverlayContent.VerticalAlignment = VerticalAlignment.Center;

        ElevatedBubbleBorder.Width = bounds is { Width: > 0 } ? bounds.Width + 8 : double.NaN;

        SelectionOverlay.IsVisible = true;
        SelectionOverlay.Opacity = 0;
        ReactionDock.IsVisible = !msg.IsOutgoing;
        SelectionOverlay.UpdateLayout();

        _ = OverlayAnimator.FadeAsync(SelectionOverlay, 0, 1, OverlayOpen);
        _ = ScaleAsync(ElevatedBubbleBorder, 1.0, 1.02, OverlayOpen, _targetAnchorX);
        _ = OverlayAnimator.SlideYAsync(ReactionDock, 8, 0, OverlayOpen, decelerate: true);
        _ = OverlayAnimator.SlideYAsync(ContextActionMenu, -8, 0, OverlayOpen, decelerate: true);
    }

    private async void OnDismissSelectionOverlay(object? sender, TappedEventArgs e)
    {
        await DismissSelectionOverlayAsync();
    }

    private async Task DismissSelectionOverlayAsync()
    {
        if (!SelectionOverlay.IsVisible)
            return;

        try
        {
            _ = ScaleAsync(ElevatedBubbleBorder, 1.02, 1.0, OverlayClose, _targetAnchorX);
            await OverlayAnimator.FadeAsync(SelectionOverlay, 1, 0, OverlayClose);
        }
        catch
        {
        }
        finally
        {
            SelectionOverlay.IsVisible = false;
            ElevatedBubbleBorder.Width = double.NaN;
        }
    }

    // A compositor scale about the bubble's near edge, like the slides in OverlayAnimator.
    private static async Task ScaleAsync(Control control, double from, double to, TimeSpan duration, double anchorX)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
            return;

        var easing = to > from ? (IEasing)new CubicEaseOut() : new CubicEaseIn();
        visual.CenterPoint = new Vector3D(control.Bounds.Width * anchorX, control.Bounds.Height / 2, 0);
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = "Scale";
        animation.InsertKeyFrame(0f, new Vector3D(from, from, 1), easing);
        animation.InsertKeyFrame(1f, new Vector3D(to, to, 1), easing);
        animation.Duration = duration;
        visual.StartAnimation("Scale", animation);

        await Task.Delay(duration + TimeSpan.FromMilliseconds(20));
        visual.StopAnimation("Scale");
        visual.Scale = new Vector3D(to, to, 1);
    }

    // The dock offers exactly the 6 emoji with Tapback verbs — the only ones
    // iPhone/Google Messages can link to the original message (see MapEmojiToVerb).
    private void OnReactionThumbTapped(object? sender, TappedEventArgs e) => ApplyReaction("👍");
    private void OnReactionHeartTapped(object? sender, TappedEventArgs e) => ApplyReaction("❤️");
    private void OnReactionJoyTapped(object? sender, TappedEventArgs e) => ApplyReaction("😂");
    private void OnReactionDislikeTapped(object? sender, TappedEventArgs e) => ApplyReaction("👎");
    private void OnReactionEmphasizeTapped(object? sender, TappedEventArgs e) => ApplyReaction("‼️");
    private void OnReactionQuestionTapped(object? sender, TappedEventArgs e) => ApplyReaction("❓");

    private async void ApplyReaction(string emoji)
    {
        if (_targetMessage is not null && Vm is not null)
        {
            TriggerHaptic();
            await Vm.SetReactionAsync(_targetMessage, emoji);
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnCopyUrlTapped(object? sender, TappedEventArgs e)
    {
        if (_targetMessage is not null && !string.IsNullOrEmpty(_targetMessage.Link))
        {
            await Clipboard.Default.SetTextAsync(_targetMessage.Link);
            TriggerHaptic();
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnCopyTextTapped(object? sender, TappedEventArgs e)
    {
        if (_targetMessage is not null)
        {
            await Clipboard.Default.SetTextAsync(_targetMessage.Body);
            TriggerHaptic();
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnMenuStarTapped(object? sender, TappedEventArgs e)
    {
        if (_targetMessage is not null && Vm is not null)
        {
            await Vm.ToggleStarAsync(_targetMessage);
            TriggerHaptic();
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnMenuSelectMoreTapped(object? sender, TappedEventArgs e)
    {
        var target = _targetMessage;
        await DismissSelectionOverlayAsync();
        if (target is not null && Vm is not null)
        {
            Vm.EnterSelectionMode(target);
            TriggerHaptic();
        }
    }

    private async void OnMenuInfoTapped(object? sender, TappedEventArgs e)
    {
        var msg = _targetMessage;
        await DismissSelectionOverlayAsync();
        if (msg is null)
            return;

        var loc = LocalizationManager.Instance;
        InfoTypeLabel.Text = msg.IsOutgoing ? loc["Chat_InfoSent"] : loc["Chat_InfoReceived"];
        InfoTimeLabel.Text = msg.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        InfoStatusLabel.Text = SmsStatusHelper.GetInfoStatus(msg.IsOutgoing, msg.HasFailed, msg.IsDelivered, msg.IsSending) switch
        {
            MessageInfoStatus.Received => loc["Chat_InfoReceived"],
            MessageInfoStatus.Delivered => loc["Chat_InfoDelivered"],
            MessageInfoStatus.Failed => loc["Chat_InfoFailed"],
            MessageInfoStatus.Sending => loc["Chat_InfoSending"],
            _ => loc["Chat_InfoSent"],
        };
        InfoSimLabel.Text = !string.IsNullOrWhiteSpace(msg.CarrierName)
            ? $"{loc["Chat_InfoSim"]} {msg.SimSlot} ({msg.CarrierName})"
            : $"{loc["Chat_InfoSim"]} {msg.SimSlot}";

        MessageInfoModal.IsVisible = true;
        MessageInfoModal.Opacity = 0;
        _ = OverlayAnimator.FadeAsync(MessageInfoModal, 0, 1, ButtonFade);
    }

    private async Task CloseInfoModalAsync()
    {
        try { await OverlayAnimator.FadeAsync(MessageInfoModal, 1, 0, ButtonFade); } catch { }
        MessageInfoModal.IsVisible = false;
    }

    private async void OnCloseInfoModalTapped(object? sender, TappedEventArgs e)
    {
        await CloseInfoModalAsync();
    }

    private async void OnMenuDeleteTapped(object? sender, TappedEventArgs e)
    {
        var target = _targetMessage;
        await DismissSelectionOverlayAsync();
        if (target is null || Vm is null)
            return;

        var loc = LocalizationManager.Instance;
        var confirm = await Dialogs.AlertAsync(
            loc["Chat_DeleteSingleConfirmTitle"],
            loc["Chat_DeleteSingleConfirmMessage"],
            loc["Chat_DeleteConfirmButton"],
            loc["Chat_Cancel"]);

        if (confirm)
        {
            await Vm.DeleteMessageAsync(target);
        }
    }

    private async void OnMenuReportTapped(object? sender, TappedEventArgs e)
    {
        var msg = _targetMessage;
        await DismissSelectionOverlayAsync();
        if (msg is not null)
            await SpamReport.SubmitAsync(msg.Body, isSpam: true);
    }

    // ---- Selection mode -----------------------------------------------------------------------

    private void OnExitSelectionMode(object? sender, TappedEventArgs e)
    {
        Vm?.ExitSelectionMode();
    }

    private void OnSelectAllTapped(object? sender, TappedEventArgs e)
    {
        Vm?.SelectAllMessages();
    }

    private async void OnCopySelectedTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is null)
            return;
        var text = Vm.GetSelectedMessagesText();
        if (!string.IsNullOrEmpty(text))
        {
            await Clipboard.Default.SetTextAsync(text);
            TriggerHaptic();
            Vm.ExitSelectionMode();
        }
    }

    private async void OnStarSelectedTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is not null)
        {
            await Vm.StarSelectedMessagesAsync();
            TriggerHaptic();
            Vm.ExitSelectionMode();
        }
    }

    private async void OnDeleteSelectedTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is null || Vm.SelectedCount == 0)
            return;

        var loc = LocalizationManager.Instance;
        var title = string.Format(loc["Chat_DeleteMultipleConfirmTitle"], Vm.SelectedCount);
        var message = string.Format(loc["Chat_DeleteMultipleConfirmMessage"], Vm.SelectedCount);

        var confirm = await Dialogs.AlertAsync(
            title,
            message,
            loc["Chat_DeleteConfirmButton"],
            loc["Chat_Cancel"]);

        if (confirm)
        {
            await Vm.DeleteSelectedMessagesAsync();
        }
    }

    public bool HandleBack()
    {
        if (HeaderMenuOverlay.IsVisible)
        {
            HeaderMenuOverlay.IsVisible = false;
            return true;
        }

        if (IsSearching)
        {
            CloseSearch();
            return true;
        }

        if (MessageInfoModal.IsVisible)
        {
            _ = CloseInfoModalAsync();
            return true;
        }

        if (SelectionOverlay.IsVisible)
        {
            _ = DismissSelectionOverlayAsync();
            return true;
        }

        if (Vm?.IsSelectionMode == true)
        {
            Vm.ExitSelectionMode();
            return true;
        }

        return false;
    }

    private static void TriggerHaptic(global::Android.Views.FeedbackConstants feedback = global::Android.Views.FeedbackConstants.ContextClick)
    {
        try
        {
            if (Platform.CurrentActivity?.Window?.DecorView is { } decor)
                decor.PerformHapticFeedback(feedback);
        }
        catch
        {
        }
    }
}
