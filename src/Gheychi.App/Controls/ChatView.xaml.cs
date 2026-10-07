using System.Windows.Input;
using Gheychi.App.Behaviors;
using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

public partial class ChatView : ContentView
{
    private bool _scrolledToEnd;
    // True while the newest message is on screen; decides if an arriving message is followed or
    // only announced by the scroll-to-bottom button.
    private bool _followTail = true;
    private ChatViewModel? _liveVm;
    private bool _initialLayoutSettled;
    internal ChatViewModel? Vm => BindingContext as ChatViewModel;
    private bool _buttonVisible;
    private bool _loadingOlder;
    private int _lastFirstVisibleIndex = -1;
    private long _lastDateUpdateTime;
    private long _lastOlderLoadTime;
    private const int OlderTriggerAhead = 12;
    private int _olderTriggerIndex = OlderTriggerAhead;

    public ICommand OpenSelectionMenuCommand { get; }
    private ChatMessage? _targetMessage;

    public ChatView()
    {
        InitializeComponent();
        OpenSelectionMenuCommand = new Command<object>(OnOpenSelectionMenu);
        var initialVm = new ChatViewModel();
        initialVm.SafeDispatcher = SafePrependItems;
        BindingContext = initialVm;

#if ANDROID
        MessagesList.HandlerChanged += (s, e) =>
        {
            if (MessagesList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                if (rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
                {
                    lm.StackFromEnd = true;
                    // Prefetch off-screen rows ahead of the fling so fast scroll-up
                    // binds already-measured views instead of inflating mid-fling.
                    lm.InitialPrefetchItemCount = 6;
                    lm.ItemPrefetchEnabled = true;
                }
                // The list fills a star row: an inserted or prepended row only re-lays out the list,
                // not the whole window (see ListTuning).
                rv.HasFixedSize = true;
                rv.SetItemViewCacheSize(25);
                // The default pool keeps 5 recycled rows per template; a chat shows about twice that, so
                // every chat switch re-created rows. View types are small consecutive ints.
                var pool = rv.GetRecycledViewPool();
                for (var viewType = 0; viewType < 24; viewType++)
                    pool.SetMaxRecycledViews(viewType, 16);
                rv.OverScrollMode = Android.Views.OverScrollMode.Never;
                // No insert/remove animations at all: a 25-row history prepend
                // must land instantly instead of animating 25 rows (visible jank).
                rv.SetItemAnimator(null);
            }
        };

        MessageEntry.HandlerChanged += (s, e) =>
        {
            if (MessageEntry.Handler?.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
            {
                editText.SetBackgroundColor(Android.Graphics.Color.Transparent);
                editText.SetPadding(0, 0, 0, 0);
                editText.SetIncludeFontPadding(false);
                editText.Gravity = Android.Views.GravityFlags.CenterVertical | Android.Views.GravityFlags.Start;
            }
        };

        MessageEntry.Focused += (s, e) =>
        {
            _scrolledToEnd = false;
            ScrollToEnd(false);
            MeasureKeyboardOffset();
        };
        MessageEntry.Unfocused += (s, e) => MeasureKeyboardOffset();

        // Keyboard compensation: track the visible window frame. Works whether
        // or not the OS honors AdjustResize, and never touches DecorView inset
        // listeners so Shell/MAUI layout stays exactly as before.
        Loaded += (s, e) => AttachKeyboardListener();
        Unloaded += (s, e) => DetachKeyboardListener();
#endif
    }

#if ANDROID
    private Android.Views.View? _keyboardAnchorView;
    private Android.Views.ViewTreeObserver.IOnGlobalLayoutListener? _keyboardLayoutListener;
    private Android.Graphics.Rect? _visibleFrameRect;

    private void AttachKeyboardListener()
    {
        DetachKeyboardListener();

        var activity = Platform.CurrentActivity;
        var content = activity?.FindViewById(Android.Resource.Id.Content);
        if (content is not Android.Views.View contentView)
            return;

        // A pure observer: GlobalLayoutListener never intercepts, blocks, or
        // consumes taps, and never modifies inset dispatch for other views.
        // It only measures the keyboard-occluded region when WE are open.
        _visibleFrameRect = new Android.Graphics.Rect();
        var listener = new KeyboardLayoutListener(this);
        _keyboardAnchorView = contentView;
        _keyboardLayoutListener = listener;
        contentView.ViewTreeObserver?.AddOnGlobalLayoutListener(listener);
    }

    private void DetachKeyboardListener()
    {
        try
        {
            var view = _keyboardAnchorView;
            var listener = _keyboardLayoutListener;
            _keyboardAnchorView = null;
            _keyboardLayoutListener = null;
            if (view?.ViewTreeObserver?.IsAlive == true && listener is not null)
                view.ViewTreeObserver.RemoveOnGlobalLayoutListener(listener);
        }
        catch
        {
        }
    }

    private sealed class KeyboardLayoutListener : Java.Lang.Object, Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly WeakReference<ChatView> _viewRef;
        public KeyboardLayoutListener(ChatView view) => _viewRef = new WeakReference<ChatView>(view);

        public void OnGlobalLayout()
        {
            if (_viewRef.TryGetTarget(out var view))
                view.MeasureKeyboardOffset();
        }
    }

    private double _keyboardOffset;
    private long _lastKeyboardMeasure;
    private bool _trailingMeasureQueued;
    private readonly int[] _chatLocation = new int[2];

    private void MeasureKeyboardOffset()
    {
        // Skip entirely (no layout work at all) when the chat overlay is closed.
        // This guarantees zero side effects on the home/list page.
        if (InputTransparent || !IsVisible || _keyboardAnchorView is not Android.Views.View root || _visibleFrameRect is not Android.Graphics.Rect frame)
            return;

        // Throttle: OnGlobalLayout fires on every layout pass — measuring costs
        // a frame query, so cap it to ~20Hz (smooth ride-up with the keyboard).
        // A skipped measure is run once the window is quiet, so the offset always
        // ends on the final value instead of the one from mid-animation.
        var now = Environment.TickCount64;
        if (now - _lastKeyboardMeasure < 50)
        {
            if (!_trailingMeasureQueued)
            {
                _trailingMeasureQueued = true;
                root.PostDelayed(() =>
                {
                    _trailingMeasureQueued = false;
                    MeasureKeyboardOffset();
                }, 60);
            }
            return;
        }
        _lastKeyboardMeasure = now;

        try
        {
            root.GetWindowVisibleDisplayFrame(frame);
            var metrics = DeviceDisplay.Current.MainDisplayInfo;
            if (metrics.Density <= 0)
                return;

            if (Handler?.PlatformView is not Android.Views.View chat || chat.Height <= 0)
                return;

            // Measured from the chat's own bottom edge, not the window's: the page around it may still be padded
            // for the navigation bar while the keyboard is up (MAUI holds inset updates back during the keyboard
            // animation), and lifting the input by the full window distance then left a strip of the page's green
            // background between the input and the keyboard. The slide translation is not part of the resting place.
            chat.GetLocationOnScreen(_chatLocation);
            var chatBottomPx = _chatLocation[1] - chat.TranslationY + chat.Height;

            var occludedDip = Math.Max(0, (chatBottomPx - frame.Bottom) / metrics.Density);
            // Ignore tiny occlusions (nav bar, tooltips) to avoid jitter.
            if (occludedDip < 100)
                occludedDip = 0;

            if (Math.Abs(_keyboardOffset - occludedDip) < 1)
                return;

            _keyboardOffset = occludedDip;
            MainThread.BeginInvokeOnMainThread(ApplyKeyboardOffset);
        }
        catch
        {
        }
    }

    private void ApplyKeyboardOffset()
    {
        if (InputTransparent || !IsVisible)
            return;

        // Bottom-only padding: header keeps its exact original position
        // (no top margin ever), the input bar lifts above the keyboard.
        PageContent.Padding = new Thickness(0, 0, 0, _keyboardOffset);

        // The search keyboard must not pull the list away from the match being read.
        if (_keyboardOffset > 0 && !IsSearching)
        {
            _scrolledToEnd = false;
            ScrollToEnd(false);
        }
    }

    private void ResetInputPadding()
    {
        // Keep the layout listener attached for the view lifetime (it early-outs
        // while the overlay is closed); only the offset is cleared here.
        _keyboardOffset = 0;
        PageContent.Padding = new Thickness(0);
    }
#else
    private double _keyboardOffset;

    private void AttachKeyboardListener()
    {
    }

    private void DetachKeyboardListener()
    {
    }

    private void ApplyKeyboardOffset()
    {
    }

    private void ResetInputPadding()
    {
        _keyboardOffset = 0;
        PageContent.Padding = new Thickness(0);
    }
#endif

    private void OnMessageEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
#if ANDROID
        if (MessageEntry.Handler?.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
        {
            if (string.IsNullOrEmpty(e.NewTextValue) || !e.NewTextValue.Contains('\n'))
            {
                editText.Gravity = Android.Views.GravityFlags.CenterVertical | Android.Views.GravityFlags.Start;
            }
            else
            {
                editText.Gravity = Android.Views.GravityFlags.Top | Android.Views.GravityFlags.Start;
            }
        }
#endif
    }

    public void SafePrependItems(Action action)
    {
#if ANDROID
        if (MessagesList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            if (rv.IsComputingLayout)
            {
                rv.Post(new Java.Lang.Runnable(action));
                return;
            }
        }
#endif
        action();
    }

    private void ScrollToTarget(int index, ScrollToPosition position)
    {
        if (Vm is null || index < 0 || index >= Vm.Items.Count)
            return;

#if ANDROID
        if (MessagesList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv &&
            rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
        {
            if (position == ScrollToPosition.Start)
            {
                lm.ScrollToPositionWithOffset(index, 0);
            }
            else
            {
                lm.ScrollToPosition(index);
            }
            return;
        }
#endif
        try
        {
            MessagesList.ScrollTo(index, position: position, animate: false);
        }
        catch
        {
        }
    }

    public void ScrollToInitialPosition(int? firstUnreadIndex)
    {
        if (Vm is null || Vm.Items.Count == 0)
        {
            _initialLayoutSettled = true;
            HideScrollToBottomButton();
            return;
        }

        var isUnread = firstUnreadIndex.HasValue && firstUnreadIndex.Value >= 0 && firstUnreadIndex.Value < Vm.Items.Count;
        var targetIndex = isUnread ? firstUnreadIndex!.Value : Vm.Items.Count - 1;
        var position = isUnread ? ScrollToPosition.Start : ScrollToPosition.End;

        ScrollToTarget(targetIndex, position);

        if (!isUnread || targetIndex >= Vm.Items.Count - 2)
        {
            HideScrollToBottomButton();
        }

        // Post-layout verification: ensure RecyclerView has measured and
        // anchored at the target before allowing history pagination.
        Dispatcher.Dispatch(() =>
        {
            ScrollToTarget(targetIndex, position);

            Dispatcher.Dispatch(() =>
            {
                _initialLayoutSettled = true;
                UpdateScrollButtonVisibility();
#if ANDROID
                if (MessagesList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    rv.Post(new Java.Lang.Runnable(UpdateScrollButtonVisibility));
                }
#endif
            });
        });
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
        {
            _scrolledToEnd = false;
            ScrollToEnd(true);
        }
        else
        {
            ShowScrollToBottomButton();
        }
    }

    private PreparedChatData? PrepareForTransitionCore(ThreadItem thread)
    {
        _followTail = true;
        _initialLayoutSettled = false;
        _scrolledToEnd = false;
        _lastFirstVisibleIndex = -1;
        _olderTriggerIndex = OlderTriggerAhead;
        _loadingOlder = false;
        HideScrollToBottomButton();

        if (Vm is not { } vm)
        {
            vm = new ChatViewModel();
            BindingContext = vm;
        }

        vm.ResetForOpen();
        vm.Thread = thread;
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
        _scrolledToEnd = false;

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
        _scrolledToEnd = false;
        _lastFirstVisibleIndex = -1;
        vm.SafeDispatcher = SafePrependItems;
        HideSkeleton();
        HideScrollToBottomButton();
        BindingContext = vm;
    }

    public void ScrollToBottom()
    {
        _scrolledToEnd = false;
        ScrollToEnd(false);
    }

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
        MessageEntry.Unfocus();
        return Task.CompletedTask;
    }

    /// <summary>Puts the cursor in the message box (and brings up the keyboard).</summary>
    public void FocusMessageInput() => MessageEntry.Focus();

    /// <summary>Takes the keyboard down, e.g. before another page covers the chat.</summary>
    public void ReleaseInputFocus() => MessageEntry.Unfocus();

    /// <summary>Called once the overlay is out of sight; resetting the padding earlier would re-lay out the chat mid-slide.</summary>
    public void ResetAfterClose() => ResetInputPadding();

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

#if ANDROID
    private Android.Animation.ObjectAnimator? _skeletonAnimator;

    // A platform alpha animation: nothing runs in managed code per frame while the chat opens.
    private void StartSkeletonPulse()
    {
        StopSkeletonPulse();
        if (SkeletonOverlay.Handler?.PlatformView is not Android.Views.View native)
            return;

        var animator = Android.Animation.ObjectAnimator.OfFloat(native, "alpha", 1f, 0.35f);
        if (animator is null)
            return;

        animator.SetDuration(550);
        animator.RepeatMode = Android.Animation.ValueAnimatorRepeatMode.Reverse;
        animator.RepeatCount = Android.Animation.ValueAnimator.Infinite;
        animator.Start();
        _skeletonAnimator = animator;
    }

    private void StopSkeletonPulse()
    {
        _skeletonAnimator?.Cancel();
        _skeletonAnimator = null;
        if (SkeletonOverlay.Handler?.PlatformView is Android.Views.View native)
            native.Alpha = 1f;
    }
#else
    private void StartSkeletonPulse()
    {
    }

    private void StopSkeletonPulse()
    {
    }
#endif

    private void OnScrollToBottomTapped(object? sender, EventArgs e)
    {
        if (Vm is null || Vm.Items.Count == 0)
            return;

        HideScrollToBottomButton();
        _scrolledToEnd = true;

        var targetIndex = Vm.Items.Count - 1;
        var distance = targetIndex - _lastFirstVisibleIndex;

        if (distance > 8)
        {
            ScrollToTarget(Math.Max(0, targetIndex - 2), ScrollToPosition.End);
            Dispatcher.Dispatch(() =>
            {
                try
                {
                    MessagesList.ScrollTo(targetIndex, position: ScrollToPosition.End, animate: true);
                }
                catch
                {
                    ScrollToTarget(targetIndex, ScrollToPosition.End);
                }
            });
        }
        else
        {
            MessagesList.ScrollTo(targetIndex, position: ScrollToPosition.End, animate: true);
        }
    }

    private bool IsAtBottom()
    {
        if (Vm is null || Vm.Items.Count == 0)
            return true;

#if ANDROID
        if (MessagesList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            if (rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager lm)
            {
                var lastVisible = lm.FindLastVisibleItemPosition();
                if (lastVisible >= Vm.Items.Count - 2)
                    return true;
            }

            if (!rv.CanScrollVertically(1))
                return true;

            return false;
        }
#endif

        return true;
    }

    private void UpdateScrollButtonVisibility()
    {
        if (Vm is null || Vm.Items.Count == 0)
        {
            HideScrollToBottomButton();
            return;
        }

        if (IsAtBottom())
        {
            HideScrollToBottomButton();
            _scrolledToEnd = true;
        }
        else
        {
            ShowScrollToBottomButton();
            _scrolledToEnd = false;
        }
    }

    private void ShowScrollToBottomButton()
    {
        if (_buttonVisible) return;
        _buttonVisible = true;
        ScrollToBottomButton.IsVisible = true;
        _ = ScrollToBottomButton.FadeToAsync(1, 150);
    }

    private void HideScrollToBottomButton()
    {
        if (!_buttonVisible) return;
        _buttonVisible = false;
        _ = ScrollToBottomButton.FadeToAsync(0, 150).ContinueWith(_ =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_buttonVisible)
                    ScrollToBottomButton.IsVisible = false;
            });
        });
    }

    private void OnMessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (Vm is null || Vm.Items.Count == 0)
            return;

        if (e.LastVisibleItemIndex >= Vm.Items.Count - 2)
        {
            _followTail = true;
            HideScrollToBottomButton();
        }
        else if (e.LastVisibleItemIndex >= 0)
        {
#if ANDROID
            if (MessagesList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv && !rv.CanScrollVertically(1))
            {
                _followTail = true;
                HideScrollToBottomButton();
            }
            else
            {
                _followTail = false;
                ShowScrollToBottomButton();
            }
#else
            _followTail = false;
            ShowScrollToBottomButton();
#endif
        }

        // Throttle sticky date updates to at most once per 180ms during fast scrolling
        if (e.FirstVisibleItemIndex != _lastFirstVisibleIndex)
        {
            var oldFirst = _lastFirstVisibleIndex;
            _lastFirstVisibleIndex = e.FirstVisibleItemIndex;
            var now = Environment.TickCount64;
            if (now - _lastDateUpdateTime > 180)
            {
                _lastDateUpdateTime = now;
                var day = Vm.GetDateForIndex(e.FirstVisibleItemIndex);
                if (!string.IsNullOrEmpty(day) && day != Vm.StickyDate)
                    Vm.StickyDate = day;
            }

            // Only trigger loading older history if:
            // 1. Initial layout and scroll have fully settled (NEVER on chat open!)
            // 2. User is actively moving UP toward older messages (oldFirst >= 0 and current < oldFirst)
            // 3. User is within trigger threshold (<= OlderTriggerAhead)
            // 4. Not currently loading, has more messages, and throttled by at least 350ms
            if (_initialLayoutSettled &&
                oldFirst >= 0 &&
                e.FirstVisibleItemIndex < oldFirst &&
                e.FirstVisibleItemIndex <= _olderTriggerIndex &&
                Vm.HasMore &&
                !_loadingOlder)
            {
                if (now - _lastOlderLoadTime >= 350)
                {
                    _lastOlderLoadTime = now;
                    TriggerLoadOlder();
                }
            }
        }
    }

    private void TriggerLoadOlder()
    {
        if (Vm == null || _loadingOlder || !_initialLayoutSettled)
            return;

        _loadingOlder = true;
        _ = Vm.LoadOlderAsync().ContinueWith(t =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _loadingOlder = false;
            });
            try
            {
                _ = t.Result;
            }
            catch
            {
            }
        });
    }

    private void OnOverlayTapEater(object? sender, TappedEventArgs e)
    {
        // Intentionally empty: existence of the recognizer is what consumes
        // the tap at the native level so it can't reach views behind us.
    }

    private async void OnBack(object? sender, EventArgs e)
    {
        if (BackRequested is not null)
            await BackRequested.Invoke();
    }

    private void OnSend(object? sender, EventArgs e)
    {
        if (Vm is null)
            return;
        var count = Vm.Items.Count;
        Vm.Send();
        if (Vm.Items.Count > count)
        {
            _followTail = true;
            _scrolledToEnd = false;
            ScrollToEnd(true);
        }
    }

    private async void OnSimTap(object? sender, EventArgs e)
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

        var choice = await Shell.Current.DisplayActionSheetAsync(
            loc["Chat_SimTitle"],
            loc["Chat_Cancel"],
            null,
            options);

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

    private void OnRetry(object? sender, TappedEventArgs e)
    {
        if (Vm is not null && (sender as View)?.BindingContext is ChatMessage message)
            Vm.Retry(message);
    }

    private long _lastSelectionToggleTick;
    private long _lastSelectionToggleMessageId;

    private void ToggleSelectionSafely(ChatMessage msg)
    {
        var now = Environment.TickCount64;
        if (msg.Id == _lastSelectionToggleMessageId && now - _lastSelectionToggleTick < 250)
            return;

        _lastSelectionToggleTick = now;
        _lastSelectionToggleMessageId = msg.Id;
        Vm?.ToggleMessageSelection(msg);
    }

    private long _lastSimToggleTick;
    private long _lastSimToggleMessageId;

    public void OnBubbleTappedDirect(ChatMessage msg)
    {
        if (Vm?.IsSelectionMode == true)
        {
            ToggleSelectionSafely(msg);
            return;
        }

        ToggleSimTagSafely(msg);
    }

    private void ToggleSimTagSafely(ChatMessage msg)
    {
        var now = Environment.TickCount64;
        if (msg.Id == _lastSimToggleMessageId && now - _lastSimToggleTick < 250)
            return;

        _lastSimToggleTick = now;
        _lastSimToggleMessageId = msg.Id;
        Vm?.ToggleSimTag(msg);
    }

    private void OnMessageRowTapped(object? sender, TappedEventArgs e)
    {
        if (Vm?.IsSelectionMode == true && (sender as View)?.BindingContext is ChatMessage msg)
        {
            ToggleSelectionSafely(msg);
        }
    }

    private void OnOutgoingBubbleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm?.IsSelectionMode == true && (sender as View)?.BindingContext is ChatMessage msg)
        {
            ToggleSelectionSafely(msg);
        }
    }

    private void OnMessageBubbleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm?.IsSelectionMode == true && (sender as View)?.BindingContext is ChatMessage message)
        {
            ToggleSelectionSafely(message);
        }
        else if (Vm is not null && (sender as View)?.BindingContext is ChatMessage normalMsg)
        {
            ToggleSimTagSafely(normalMsg);
        }
    }

    private async void OnReactionPillTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as View)?.BindingContext is ChatMessage msg && Vm is not null)
        {
            if (msg.HasReactionFailed)
            {
                TriggerLightHaptic();
                await Vm.RetryReactionAsync(msg);
            }
        }
    }

    private void OnMenuTapEater(object? sender, EventArgs e)
    {
    }

    private void OnOpenSelectionMenu(object? param)
    {
        if (param is ValueTuple<object, MessageBounds> tuple && tuple.Item1 is ChatMessage tupleMsg)
        {
            OpenSelectionMenu(tupleMsg, tuple.Item2);
        }
        else if (param is ChatMessage directMsg)
        {
            OpenSelectionMenu(directMsg);
        }
    }

    public void OpenSelectionMenu(ChatMessage msg, MessageBounds? bounds = null)
    {
        if (msg is null)
            return;

        if (Vm?.IsSelectionMode == true)
        {
            ToggleSelectionSafely(msg);
            return;
        }

        _targetMessage = msg;

        ElevatedBubbleBody.Text = msg.BodyBeforeLink;
        if (msg.HasLink)
        {
            ElevatedBubbleLink.Text = msg.Link;
            ElevatedBubbleLink.IsVisible = true;
        }
        else
        {
            ElevatedBubbleLink.IsVisible = false;
        }

        ElevatedBubbleTime.Text = msg.Time;

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        if (msg.IsOutgoing)
        {
            ElevatedBubbleBorder.BackgroundColor = Color.FromArgb("#2E6B4C");
            ElevatedBubbleBody.TextColor = Colors.White;
            ElevatedBubbleBorder.HorizontalOptions = LayoutOptions.End;
            ElevatedBubbleBorder.AnchorX = 1.0;
            ElevatedMetadataGroup.HorizontalOptions = LayoutOptions.End;
            ReactionDock.HorizontalOptions = LayoutOptions.End;
            ReactionDock.AnchorX = 1.0;
            ContextActionMenu.HorizontalOptions = LayoutOptions.End;
            ContextActionMenu.AnchorX = 1.0;
        }
        else
        {
            ElevatedBubbleBorder.BackgroundColor = isDark ? Color.FromArgb("#1C1F24") : Colors.White;
            ElevatedBubbleBody.TextColor = isDark ? Color.FromArgb("#E8EAED") : Color.FromArgb("#1B1E24");
            ElevatedBubbleBorder.HorizontalOptions = LayoutOptions.Start;
            ElevatedBubbleBorder.AnchorX = 0.0;
            ElevatedMetadataGroup.HorizontalOptions = LayoutOptions.Start;
            ReactionDock.HorizontalOptions = LayoutOptions.Start;
            ReactionDock.AnchorX = 0.0;
            ContextActionMenu.HorizontalOptions = LayoutOptions.Start;
            ContextActionMenu.AnchorX = 0.0;
        }

        ElevatedBubbleBorder.AnchorY = 0.5;

        CopyUrlActionRow.IsVisible = msg.HasLink;
        MenuReportRow.IsVisible = !msg.IsOutgoing;

        var loc = LocalizationManager.Instance;
        MenuStarLabel.Text = msg.IsStarred ? loc["Chat_Unstar"] : loc["Chat_Star"];
        MenuStarIcon.Source = msg.IsStarred ? "starfill.png" : "star.png";

        var screenH = Height > 0 ? Height : 800.0;
        var bubbleY = bounds?.Y ?? (screenH * 0.4);

        if (bubbleY > screenH * 0.52)
        {
            OverlayContent.VerticalOptions = LayoutOptions.End;
        }
        else if (bubbleY < screenH * 0.28)
        {
            OverlayContent.VerticalOptions = LayoutOptions.Start;
        }
        else
        {
            OverlayContent.VerticalOptions = LayoutOptions.Center;
        }

        OverlayContent.TranslationY = 0;

        if (bounds != null && bounds.Width > 0)
        {
            ElevatedBubbleBorder.WidthRequest = bounds.Width + 8;
        }
        else
        {
            ElevatedBubbleBorder.WidthRequest = -1;
        }

        SelectionOverlay.IsVisible = true;
        SelectionOverlay.Opacity = 0;
        ReactionDock.IsVisible = !msg.IsOutgoing;
        ReactionDock.TranslationY = 8;
        ContextActionMenu.TranslationY = -8;
        ElevatedBubbleBorder.Scale = 1.0;

        _ = SelectionOverlay.FadeToAsync(1, 180, Easing.CubicOut);
        _ = ElevatedBubbleBorder.ScaleToAsync(1.02, 180, Easing.CubicOut);
        _ = ReactionDock.TranslateToAsync(0, 0, 180, Easing.CubicOut);
        _ = ContextActionMenu.TranslateToAsync(0, 0, 180, Easing.CubicOut);
    }

    private async void OnDismissSelectionOverlay(object? sender, EventArgs e)
    {
        await DismissSelectionOverlayAsync();
    }

    private async Task DismissSelectionOverlayAsync()
    {
        if (!SelectionOverlay.IsVisible)
            return;

        try
        {
            _ = ElevatedBubbleBorder.ScaleToAsync(1.0, 150, Easing.CubicIn);
            await SelectionOverlay.FadeToAsync(0, 150, Easing.CubicIn);
        }
        catch
        {
        }
        finally
        {
            SelectionOverlay.IsVisible = false;
            ElevatedBubbleBorder.WidthRequest = -1;
        }
    }

    // The dock offers exactly the 6 emoji with Tapback verbs — the only ones
    // iPhone/Google Messages can link to the original message (see MapEmojiToVerb).
    private void OnReactionThumbTapped(object? sender, EventArgs e) => ApplyReaction("👍");
    private void OnReactionHeartTapped(object? sender, EventArgs e) => ApplyReaction("❤️");
    private void OnReactionJoyTapped(object? sender, EventArgs e) => ApplyReaction("😂");
    private void OnReactionDislikeTapped(object? sender, EventArgs e) => ApplyReaction("👎");
    private void OnReactionEmphasizeTapped(object? sender, EventArgs e) => ApplyReaction("‼️");
    private void OnReactionQuestionTapped(object? sender, EventArgs e) => ApplyReaction("❓");

    private async void ApplyReaction(string emoji)
    {
        if (_targetMessage is not null && Vm is not null)
        {
            TriggerLightHaptic();
            await Vm.SetReactionAsync(_targetMessage, emoji);
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnCopyUrlTapped(object? sender, EventArgs e)
    {
        if (_targetMessage is not null && !string.IsNullOrEmpty(_targetMessage.Link))
        {
            await Clipboard.Default.SetTextAsync(_targetMessage.Link);
            TriggerLightHaptic();
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnCopyTextTapped(object? sender, EventArgs e)
    {
        if (_targetMessage is not null)
        {
            await Clipboard.Default.SetTextAsync(_targetMessage.FullBody);
            TriggerLightHaptic();
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnMenuStarTapped(object? sender, EventArgs e)
    {
        if (_targetMessage is not null && Vm is not null)
        {
            await Vm.ToggleStarAsync(_targetMessage);
            TriggerLightHaptic();
        }
        await DismissSelectionOverlayAsync();
    }

    private async void OnMenuSelectMoreTapped(object? sender, EventArgs e)
    {
        var target = _targetMessage;
        await DismissSelectionOverlayAsync();
        if (target is not null && Vm is not null)
        {
            Vm.EnterSelectionMode(target);
            TriggerLightHaptic();
        }
    }

    private async void OnMenuInfoTapped(object? sender, EventArgs e)
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
        _ = MessageInfoModal.FadeToAsync(1, 150);
    }

    private async Task CloseInfoModalAsync()
    {
        try { await MessageInfoModal.FadeToAsync(0, 150); } catch { }
        MessageInfoModal.IsVisible = false;
    }

    private async void OnCloseInfoModalTapped(object? sender, EventArgs e)
    {
        await CloseInfoModalAsync();
    }

    private async void OnMenuDeleteTapped(object? sender, EventArgs e)
    {
        var target = _targetMessage;
        await DismissSelectionOverlayAsync();
        if (target is null || Vm is null)
            return;

        var loc = LocalizationManager.Instance;
        var confirm = await Shell.Current.DisplayAlertAsync(
            loc["Chat_DeleteSingleConfirmTitle"],
            loc["Chat_DeleteSingleConfirmMessage"],
            loc["Chat_DeleteConfirmButton"],
            loc["Chat_Cancel"]);

        if (confirm)
        {
            await Vm.DeleteMessageAsync(target);
        }
    }

    private void OnExitSelectionMode(object? sender, EventArgs e)
    {
        Vm?.ExitSelectionMode();
    }

    private void OnSelectAllTapped(object? sender, EventArgs e)
    {
        Vm?.SelectAllMessages();
    }

    private async void OnCopySelectedTapped(object? sender, EventArgs e)
    {
        if (Vm is null)
            return;
        var text = Vm.GetSelectedMessagesText();
        if (!string.IsNullOrEmpty(text))
        {
            await Clipboard.Default.SetTextAsync(text);
            TriggerLightHaptic();
            Vm.ExitSelectionMode();
        }
    }

    private async void OnMenuReportTapped(object? sender, EventArgs e)
    {
        var msg = _targetMessage;
        await DismissSelectionOverlayAsync();
        if (msg is not null)
            await SpamReport.SubmitAsync($"{msg.BodyBeforeLink}{msg.Link}", isSpam: true);
    }

    private async void OnStarSelectedTapped(object? sender, EventArgs e)
    {
        if (Vm is not null)
        {
            await Vm.StarSelectedMessagesAsync();
            TriggerLightHaptic();
            Vm.ExitSelectionMode();
        }
    }

    private async void OnDeleteSelectedTapped(object? sender, EventArgs e)
    {
        if (Vm is null || Vm.SelectedCount == 0)
            return;

        var loc = LocalizationManager.Instance;
        var title = string.Format(loc["Chat_DeleteMultipleConfirmTitle"], Vm.SelectedCount);
        var message = string.Format(loc["Chat_DeleteMultipleConfirmMessage"], Vm.SelectedCount);

        var confirm = await Shell.Current.DisplayAlertAsync(
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

    private static void TriggerLightHaptic()
    {
        try
        {
#if ANDROID
            if (Platform.CurrentActivity?.Window?.DecorView is Android.Views.View decor)
                decor.PerformHapticFeedback(Android.Views.FeedbackConstants.ContextClick);
#endif
        }
        catch
        {
        }
    }

    private void ScrollToEnd(bool animate)
    {
        if (_scrolledToEnd || SuppressAutoScroll || Vm is null || Vm.Items.Count == 0)
            return;
        try
        {
            ScrollToTarget(Vm.Items.Count - 1, ScrollToPosition.End);
            HideScrollToBottomButton();
            _scrolledToEnd = animate || MessagesList.Height > 0;
        }
        catch (InvalidOperationException)
        {
        }
    }
}
