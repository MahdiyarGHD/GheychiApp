using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public sealed record PreparedChatData(
    List<object> Items,
    List<ChatMessage> Messages,
    List<(int Index, string Text)> Separators,
    string StickyDate,
    int SimSlot,
    int? FirstUnreadIndex = null
);

public sealed class ChatViewModel : INotifyPropertyChanged
{
    private const int RecentPageSize = 25;

    private static readonly Dictionary<long, PreparedChatData> RecentCache = [];
    private static readonly List<long> CacheOrder = [];
    private static readonly object CacheLock = new();
    private static CancellationTokenSource? _preloadCts;
    private static readonly object PreloadLock = new();
    private static IReadOnlyDictionary<int, int>? _simSlotMap;
    private static IReadOnlyDictionary<int, string>? _simCarrierMap;
    private static IReadOnlyList<SimCardInfo>? _activeSims;
    private static bool? _isDualSim;
    private static Task? _simLoadTask;
    private static readonly object SimInitLock = new();

    public IReadOnlyList<SimCardInfo> ActiveSims => _activeSims ?? Array.Empty<SimCardInfo>();

    private readonly ISmsService _smsService;
    private readonly IDateFormattingService _dateFormatter;
    private readonly IMessageMetadataRepository _metadataRepo;
    private readonly List<(int Index, string Text)> _dateSeparators = [];
    private string _draft = string.Empty;
    private int _simSlot = 1;
    private string _stickyDate = string.Empty;
    private ThreadItem _thread;
    private int _loadedCount;
    private bool _hasMore = true;
    private bool _loadingOlder;

    public ChatViewModel(ThreadItem? thread = null, ISmsService? smsService = null, IDateFormattingService? dateFormatter = null, IMessageMetadataRepository? metadataRepo = null)
    {
        _smsService = smsService ?? IPlatformApplication.Current?.Services.GetService<ISmsService>() ?? throw new InvalidOperationException("ISmsService not resolved");
        _dateFormatter = dateFormatter ?? IPlatformApplication.Current?.Services.GetService<IDateFormattingService>() ?? new DateFormattingService();
        _metadataRepo = metadataRepo ?? IPlatformApplication.Current?.Services.GetService<IMessageMetadataRepository>() ?? new Gheychi.Infrastructure.Data.MessageMetadataRepository();

        _thread = thread ?? ThreadItem.Empty;
        Items = [];
        Messages = [];

        _ = EnsureSimInfoLoadedAsync();

        if (thread != null && thread.ThreadId > 0)
        {
            _ = LoadMessagesAsync(thread);
        }
    }

    public ThreadItem Thread
    {
        get => _thread;
        set => SetField(ref _thread, value);
    }

    public FastObservableCollection<object> Items { get; }
    public FastObservableCollection<ChatMessage> Messages { get; }

    public string Draft
    {
        get => _draft;
        set => SetField(ref _draft, value);
    }

    public string StickyDate
    {
        get => _stickyDate;
        set
        {
            if (_stickyDate != value)
            {
                SetField(ref _stickyDate, value);
                OnPropertyChanged(nameof(HasStickyDate));
            }
        }
    }

    public bool HasStickyDate => !string.IsNullOrWhiteSpace(_stickyDate);

    public int? FirstUnreadIndex { get; private set; }

    public Action<Action>? SafeDispatcher { get; set; }

    private static readonly Color[] SimBadgePalette =
    [
        Color.FromArgb("#0F766E"), // Teal
        Color.FromArgb("#B45309"), // Amber
        Color.FromArgb("#4338CA"), // Indigo
        Color.FromArgb("#7C3AED"), // Purple
    ];

    public string SimText => _simSlot.ToString();

    public Color SimBadgeColor => SimBadgePalette[Math.Max(0, (_simSlot - 1) % SimBadgePalette.Length)];

    public bool HasMore => _hasMore;

    public static void AddToCache(long threadId, PreparedChatData data)
    {
        lock (CacheLock)
        {
            if (RecentCache.ContainsKey(threadId))
            {
                RecentCache[threadId] = data;
                CacheOrder.Remove(threadId);
                CacheOrder.Add(threadId);
                return;
            }

            if (RecentCache.Count >= 80 && CacheOrder.Count > 0)
            {
                var oldest = CacheOrder[0];
                CacheOrder.RemoveAt(0);
                RecentCache.Remove(oldest);
            }

            RecentCache[threadId] = data;
            CacheOrder.Add(threadId);
        }
    }

    public static void PreloadVisibleThreads(IEnumerable<ThreadItem> threads, ISmsService smsService, IDateFormattingService dateFormatter, int maxThreads = 15)
    {
        lock (PreloadLock)
        {
            _preloadCts?.Cancel();
            _preloadCts?.Dispose();
            _preloadCts = new CancellationTokenSource();
        }

        var ct = _preloadCts.Token;
        _ = Task.Run(async () =>
        {
            var count = 0;
            foreach (var thread in threads)
            {
                if (ct.IsCancellationRequested || count++ >= maxThreads)
                    break;

                lock (CacheLock)
                {
                    if (RecentCache.ContainsKey(thread.ThreadId))
                        continue;
                }

                // Lightly throttled background warm-up: the open chat's own query
                // always has SQLite priority, so list warm-up never blocks it.
                try
                {
                    await Task.Delay(150, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    var loader = new ChatViewModel(null, smsService, dateFormatter);
                    var data = await loader.FetchMessagesAsync(thread, RecentPageSize, cancellationToken: ct);
                    if (!ct.IsCancellationRequested)
                    {
                        AddToCache(thread.ThreadId, data);
                    }
                }
                catch
                {
                }
            }
        }, ct);
    }

    public static bool TryGetCached(long threadId, out PreparedChatData? data)
    {
        lock (CacheLock)
        {
            if (RecentCache.TryGetValue(threadId, out data))
            {
                CacheOrder.Remove(threadId);
                CacheOrder.Add(threadId);
                return true;
            }
            return false;
        }
    }

    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            RecentCache.Clear();
            CacheOrder.Clear();
        }
    }

    public string GetDateForIndex(int firstVisibleIndex)
    {
        if (_dateSeparators.Count == 0)
            return StickyDate;

        var result = _dateSeparators[0].Text;
        for (var i = 0; i < _dateSeparators.Count; i++)
        {
            if (_dateSeparators[i].Index <= firstVisibleIndex)
                result = _dateSeparators[i].Text;
            else
                break;
        }
        return result;
    }

    public async Task<PreparedChatData> FetchMessagesAsync(ThreadItem thread, int? limit = RecentPageSize, int offset = 0, CancellationToken cancellationToken = default)
    {
        await EnsureSimInfoLoadedAsync();
        var isDual = _isDualSim ?? false;
        var slotMap = _simSlotMap;
        var carrierMap = _simCarrierMap;

        var fetchLimit = limit;
        if (offset == 0 && thread.IsUnread && thread.Count > (limit ?? RecentPageSize))
        {
            fetchLimit = Math.Min(thread.Count + 10, 100);
        }

        var rawMessages = await _smsService.GetMessagesAsync(thread.ThreadId, fetchLimit, offset, cancellationToken);
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentUICulture;

        DateTime? lastDate = null;
        var newItems = new List<object>(rawMessages.Count * 2);
        var newMessages = new List<ChatMessage>(rawMessages.Count);
        var seps = new List<(int Index, string Text)>();
        int? firstUnreadIndex = null;
        var unreadInserted = false;

        foreach (var msg in rawMessages)
        {
            if (lastDate == null || msg.Timestamp.Date != lastDate.Value.Date)
            {
                var sepText = _dateFormatter.FormatDateSeparator(msg.Timestamp, now, culture);
                seps.Add((newItems.Count, sepText));
                newItems.Add(new DateSeparatorItem(sepText));
                lastDate = msg.Timestamp.Date;
            }

            if (!unreadInserted && !msg.IsRead && !msg.IsOutgoing)
            {
                unreadInserted = true;
                firstUnreadIndex = newItems.Count;
                var unreadLabel = Localization.LocalizationManager.Instance["Chat_UnreadMessages"];
                newItems.Add(new UnreadSeparatorItem(unreadLabel));
            }

            var (bodyBefore, link) = LinkDetector.ExtractLink(msg.Body);
            var timeStr = _dateFormatter.FormatMessageTime(msg.Timestamp, culture);
            var slot = ResolveSlot(msg.SubId, slotMap);
            var carrier = ResolveCarrier(msg.SubId, slot, carrierMap);

            var chatMsg = new ChatMessage
            {
                Id = msg.Id,
                Timestamp = msg.Timestamp,
                BodyBeforeLink = bodyBefore,
                Link = link,
                IsOutgoing = msg.IsOutgoing,
                IsDelivered = msg.IsDelivered,
                HasFailed = msg.HasFailed,
                Time = timeStr,
                IsUnread = !msg.IsRead,
                IsStarred = msg.IsStarred,
                ReactionEmoji = msg.Reaction,
                SubId = msg.SubId,
                SimSlot = slot,
                CarrierName = carrier,
                IsDualSim = isDual,
                IsSelectionMode = _isSelectionMode
            };

            newItems.Add(chatMsg);
            newMessages.Add(chatMsg);
        }

        if (offset == 0)
        {
            var lastIncoming = newMessages.LastOrDefault(m => !m.IsOutgoing);
            if (lastIncoming != null)
                lastIncoming.IsLastMessage = true;
        }

        var lastSep = newItems.OfType<DateSeparatorItem>().LastOrDefault();
        var sticky = lastSep?.Text ?? _dateFormatter.FormatStickyDate(now, now, culture);
        var threadSlot = (thread.SubId > 0 && slotMap != null && slotMap.TryGetValue(thread.SubId, out var s)) ? s : 1;

        return new PreparedChatData(newItems, newMessages, seps, sticky, threadSlot, firstUnreadIndex);
    }

    private Task<PreparedChatData?>? _prefetchTask;
    private CancellationTokenSource? _prefetchCts;
    private long _prefetchThreadId;
    private int _prefetchOffset = -1;

    public static int PageSize => RecentPageSize;

    public void ResetForOpen()
    {
        try { _prefetchCts?.Cancel(); } catch { }
        _prefetchCts?.Dispose();
        _prefetchCts = null;
        _hasMore = true;
        _loadedCount = 0;
        _prefetchTask = null;
        _prefetchOffset = -1;
        _prefetchThreadId = 0;
        FirstUnreadIndex = null;
    }

    public void StartPrefetchOlder(int? offsetOverride = null)
    {
        // NOTE: no _loadingOlder check here on purpose. This method only queues
        // a background fetch; it is called from inside LoadOlderAsync while
        // _loadingOlder is still set, and gating on it broke the prefetch chain
        // after page 2 (every later page became a cold fetch mid-scroll).
        if (!_hasMore)
            return;

        var thread = Thread;
        if (thread.ThreadId <= 0)
            return;

        if (_prefetchTask != null)
        {
            if (_prefetchThreadId == thread.ThreadId)
                return; // already fetching ahead for this thread
            // Stale fetch for a previous thread — cancel and replace.
            try { _prefetchCts?.Cancel(); } catch { }
            _prefetchTask = null;
            _prefetchOffset = -1;
        }

        var offset = offsetOverride ?? _loadedCount;
        _prefetchThreadId = thread.ThreadId;
        _prefetchOffset = offset;
        _prefetchCts?.Dispose();
        _prefetchCts = new CancellationTokenSource();
        var ct = _prefetchCts.Token;
        _prefetchTask = Task.Run(async () =>
        {
            try
            {
                return await FetchMessagesAsync(thread, RecentPageSize, offset, ct);
            }
            catch
            {
                return null;
            }
        }, ct);
    }

    public static void CancelPreload()
    {
        lock (PreloadLock)
        {
            _preloadCts?.Cancel();
        }
    }

    private async Task EnsureSimInfoLoadedAsync()
    {
        Task task;
        lock (SimInitLock)
        {
            if (_simSlotMap != null && _simCarrierMap != null && _isDualSim.HasValue && _activeSims != null)
                return;

            _simLoadTask ??= Task.Run(async () =>
            {
                try
                {
                    var slotMap = await _smsService.GetSimSlotMapAsync();
                    var carrierMap = await _smsService.GetSimCarrierMapAsync();
                    var isDual = await _smsService.IsDualSimAsync();
                    var simList = await _smsService.GetActiveSimsAsync();
                    _simSlotMap = slotMap;
                    _simCarrierMap = carrierMap;
                    _isDualSim = isDual;
                    _activeSims = simList;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnPropertyChanged(nameof(ActiveSims));
                        UpdateMessageSimSlots();
                    });
                }
                catch
                {
                }
            });
            task = _simLoadTask;
        }

        try
        {
            await task;
        }
        catch
        {
        }
    }

    private void UpdateMessageSimSlots()
    {
        var isDual = _isDualSim ?? false;
        var slotMap = _simSlotMap;
        var carrierMap = _simCarrierMap;

        foreach (var item in Items)
        {
            if (item is ChatMessage msg)
            {
                msg.IsDualSim = isDual;
                var slot = ResolveSlot(msg.SubId, slotMap);
                msg.SimSlot = slot;
                var carrier = ResolveCarrier(msg.SubId, slot, carrierMap);
                if (!string.IsNullOrWhiteSpace(carrier))
                    msg.CarrierName = carrier;
            }
        }
    }

    private static int ResolveSlot(int subId, IReadOnlyDictionary<int, int>? map)
    {
        if (subId <= 0)
            return 1;
        if (map != null && map.TryGetValue(subId, out var slot))
            return slot;
        return subId > 1 ? 2 : 1;
    }

    private static string? ResolveCarrier(int subId, int slot, IReadOnlyDictionary<int, string>? carrierMap)
    {
        if (carrierMap == null || carrierMap.Count == 0)
            return null;
        if (subId > 0 && carrierMap.TryGetValue(subId, out var carrier) && !string.IsNullOrWhiteSpace(carrier))
            return carrier;
        if (carrierMap.TryGetValue(slot, out var bySlot) && !string.IsNullOrWhiteSpace(bySlot))
            return bySlot;
        return null;
    }

    public void ToggleSimTag(ChatMessage message)
    {
        message.ToggleRevealed();
    }

    public void ApplyMessages(PreparedChatData data, int rawCount)
    {
        var threadSubId = Thread.SubId > 0 ? Thread.SubId : 0;
        SelectSim(data.SimSlot, threadSubId);

        FirstUnreadIndex = data.FirstUnreadIndex;
        _loadedCount = rawCount;
        _hasMore = rawCount >= RecentPageSize;

        _dateSeparators.Clear();
        _dateSeparators.AddRange(data.Separators);

        var isDual = _isDualSim ?? false;
        var slotMap = _simSlotMap;
        var carrierMap = _simCarrierMap;

        foreach (var item in data.Items)
        {
            if (item is ChatMessage msg)
            {
                msg.IsDualSim = isDual;
                var slot = ResolveSlot(msg.SubId, slotMap);
                msg.SimSlot = slot;
                var carrier = ResolveCarrier(msg.SubId, slot, carrierMap);
                if (!string.IsNullOrWhiteSpace(carrier))
                    msg.CarrierName = carrier;
            }
        }

        var lastIncoming = data.Messages.LastOrDefault(m => !m.IsOutgoing);
        if (lastIncoming != null)
            lastIncoming.IsLastMessage = true;

        Items.Reset(data.Items);
        Messages.Reset(data.Messages);
        StickyDate = data.StickyDate;

        // Pre-warm the NEXT page right away, in the background, so history is
        // already in memory long before the user scrolls up to it.
        // StartPrefetchOlder is idempotent — safe to call unconditionally.
        StartPrefetchOlder();
    }

    public async Task LoadMessagesAsync(ThreadItem thread)
    {
        Thread = thread;

        if (TryGetCached(thread.ThreadId, out var cached) && cached is not null)
        {
            ApplyMessages(cached, cached.Messages.Count);
            return;
        }

        var data = await FetchMessagesAsync(thread);
        ApplyMessages(data, data.Messages.Count);
    }

    public async Task<bool> LoadOlderAsync()
    {
        if (_loadingOlder || !_hasMore)
            return false;

        // Capture the thread up front: if the user opens a different chat while
        // this fetch is in flight, the result must be discarded, never prepended
        // into the new thread's list.
        var threadId = Thread.ThreadId;
        _loadingOlder = true;
        try
        {
            PreparedChatData? page = null;
            var pending = _prefetchTask;
            var pendingOffset = _prefetchOffset;
            if (pending != null)
            {
                _prefetchTask = null;
                _prefetchOffset = -1;
                try
                {
                    // Only reuse the prefetched page if it still belongs to this
                    // thread and the list hasn't moved past it.
                    page = (_prefetchThreadId == Thread.ThreadId && pendingOffset == _loadedCount) ? await pending : null;
                }
                catch
                {
                    page = null;
                }
            }

            page ??= await FetchMessagesAsync(Thread, RecentPageSize, _loadedCount);

            if (page == null || page.Messages.Count == 0)
            {
                _hasMore = false;
                return false;
            }

            var count = page.Messages.Count;
            // Guard runs INSIDE the main-thread lambda: Thread is only ever
            // reassigned on the main thread too, so this check is race-free.
            // A stale page can never land in a newly opened chat's list.
            var stillCurrent = false;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                stillCurrent = Thread.ThreadId == threadId;
                if (stillCurrent)
                {
                    if (SafeDispatcher != null)
                        SafeDispatcher(() => PrependOlder(page, count));
                    else
                        PrependOlder(page, count);
                }
            });
            if (!stillCurrent)
                return false;

            // Chain the NEXT page immediately so history stays one step ahead
            // of the fling. Idempotent — safe to call unconditionally.
            StartPrefetchOlder();

            return true;
        }
        finally
        {
            _loadingOlder = false;
        }
    }

    private void PrependOlder(PreparedChatData page, int rawCount)
    {
        _loadedCount += rawCount;
        if (rawCount < RecentPageSize)
            _hasMore = false;

        var pageItems = page.Items;
        if (pageItems.Count > 0 && Items.Count > 0 &&
            pageItems[^1] is DateSeparatorItem tail &&
            Items[0] is DateSeparatorItem head &&
            tail.Text == head.Text)
        {
            pageItems = pageItems.Take(pageItems.Count - 1).ToList();
        }

        // Incremental prepend: single range-insert notification so the
        // RecyclerView shifts existing rows instead of rebinding everything.
        Items.PrependRange(pageItems);
        Messages.PrependRange(page.Messages);

        var trimmedSepCount = pageItems.Count < page.Items.Count ? 1 : 0;
        var shift = pageItems.Count;
        var pageSeps = trimmedSepCount > 0 ? page.Separators.Take(page.Separators.Count - 1) : page.Separators;
        var mergedSeps = new List<(int Index, string Text)>(page.Separators.Count + _dateSeparators.Count);
        mergedSeps.AddRange(pageSeps);
        foreach (var (index, text) in _dateSeparators)
            mergedSeps.Add((index + shift, text));

        _dateSeparators.Clear();
        _dateSeparators.AddRange(mergedSeps);

        lock (CacheLock)
        {
            RecentCache.Remove(Thread.ThreadId);
        }
    }

    private int _simSubId;

    private string SendAddress => PhoneNumberNormalizer.ToSendAddress(Thread.Phone);

    private int ResolveSubId(int messageSubId) =>
        SimResolver.ResolveSendSubId(messageSubId, _simSubId, _simSlot, _simSlotMap);

    public void ToggleSim()
    {
        var sims = ActiveSims;
        if (sims.Count <= 1)
            return;

        var currentIndex = 0;
        for (var i = 0; i < sims.Count; i++)
        {
            if (sims[i].SlotIndex == _simSlot)
            {
                currentIndex = i;
                break;
            }
        }
        var next = sims[(currentIndex + 1) % sims.Count];
        SelectSim(next.SlotIndex, next.SubId);
    }

    public void SelectSim(int slot, int subId = 0)
    {
        if (slot <= 0)
            return;
        _simSlot = slot;
        _simSubId = subId > 0 ? subId : (_simSlotMap != null ? _simSlotMap.FirstOrDefault(kvp => kvp.Value == slot).Key : slot);
        OnPropertyChanged(nameof(SimText));
        OnPropertyChanged(nameof(SimBadgeColor));
    }

    public async void Send()
    {
        if (string.IsNullOrWhiteSpace(Draft))
            return;

        var text = Draft.Trim();
        Draft = string.Empty;

        var timeStr = _dateFormatter.FormatMessageTime(DateTime.Now, CultureInfo.CurrentUICulture);
        var (before, link) = LinkDetector.ExtractLink(text);

        var outgoingMsg = new ChatMessage
        {
            BodyBeforeLink = before,
            Link = link,
            IsOutgoing = true,
            IsDelivered = false,
            Time = timeStr,
            SimSlot = _simSlot,
            IsDualSim = _isDualSim ?? false
        };

        Items.Add(outgoingMsg);
        Messages.Add(outgoingMsg);

        lock (CacheLock)
        {
            RecentCache.Remove(Thread.ThreadId);
        }

        var success = await _smsService.SendSmsAsync(SendAddress, text, ResolveSubId(0));
        if (success)
        {
            outgoingMsg.IsDelivered = true;
        }
        else
        {
            outgoingMsg.HasFailed = true;
        }
    }

    public async void Retry(ChatMessage message)
    {
        message.HasFailed = false;
        message.IsDelivered = false;
        var text = string.IsNullOrEmpty(message.Link) ? message.BodyBeforeLink : $"{message.BodyBeforeLink}{message.Link}";
        var success = await _smsService.SendSmsAsync(SendAddress, text, ResolveSubId(0));
        if (success)
        {
            message.IsDelivered = true;
        }
        else
        {
            message.HasFailed = true;
        }
    }

    private bool _isSelectionMode;
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        set
        {
            if (SetField(ref _isSelectionMode, value))
            {
                OnPropertyChanged(nameof(IsNotSelectionMode));
                foreach (var msg in Messages)
                {
                    msg.IsSelectionMode = value;
                    if (!value)
                        msg.IsSelected = false;
                }
                UpdateSelectedCount();
            }
        }
    }

    public bool IsNotSelectionMode => !IsSelectionMode;

    private int _selectedCount;
    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetField(ref _selectedCount, value))
            {
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(HasSelectedMessages));
            }
        }
    }

    public bool HasSelectedMessages => _selectedCount > 0;

    public string SelectedCountText
    {
        get
        {
            var loc = Localization.LocalizationManager.Instance;
            return string.Format(loc["Chat_SelectedCount"], _selectedCount);
        }
    }

    public void EnterSelectionMode(ChatMessage initialMessage)
    {
        IsSelectionMode = true;
        initialMessage.IsSelected = true;
        UpdateSelectedCount();
    }

    public void ExitSelectionMode()
    {
        IsSelectionMode = false;
    }

    public void ToggleMessageSelection(ChatMessage message)
    {
        if (!IsSelectionMode)
            return;

        message.IsSelected = !message.IsSelected;
        UpdateSelectedCount();

        if (_selectedCount == 0)
        {
            ExitSelectionMode();
        }
    }

    public void SelectAllMessages()
    {
        foreach (var msg in Messages)
            msg.IsSelected = true;
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = Messages.Count(m => m.IsSelected);
    }

    public async Task ToggleStarAsync(ChatMessage message)
    {
        var newState = !message.IsStarred;
        message.IsStarred = newState;
        await _metadataRepo.SetStarredAsync(message.Id, Thread.ThreadId, newState);
    }

    public async Task StarSelectedMessagesAsync()
    {
        var selected = Messages.Where(m => m.IsSelected).ToList();
        var allStarred = selected.All(m => m.IsStarred);
        var targetState = !allStarred;

        foreach (var msg in selected)
        {
            msg.IsStarred = targetState;
            await _metadataRepo.SetStarredAsync(msg.Id, Thread.ThreadId, targetState);
        }
    }

    public async Task SetReactionAsync(ChatMessage message, string emoji)
    {
        if (message.IsOutgoing)
            return;
        // Fail fast on unmapped emoji (the dock only offers the 6 mapped ones):
        // don't touch local state at all.
        if (ReactionHelper.MapEmojiToVerb(emoji) is null)
            return;

        var isToggleOff = message.ReactionEmoji == emoji;
        var newEmoji = isToggleOff ? null : emoji;
        message.ReactionEmoji = newEmoji;

        await _metadataRepo.SetReactionAsync(message.Id, Thread.ThreadId, newEmoji, fromMe: true);

        if (!isToggleOff && !string.IsNullOrWhiteSpace(newEmoji))
        {
            message.IsReactionSending = true;
            message.HasReactionFailed = false;

            // Always the iPhone-compatible English template (verbs + curly quotes):
            // no other SMS app recognizes any other template.
            var reactionText = ReactionHelper.FormatReactionSms(newEmoji, message.FullBody);

            var targetSubId = ResolveSubId(message.SubId);
            var address = SendAddress;

            _ = Task.Run(async () =>
            {
                try
                {
                    var success = await _smsService.SendSmsAsync(address, reactionText, targetSubId);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        message.IsReactionSending = false;
                        message.HasReactionFailed = !success;
                    });
                }
                catch
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        message.IsReactionSending = false;
                        message.HasReactionFailed = true;
                    });
                }
            });
        }
        else
        {
            message.IsReactionSending = false;
            message.HasReactionFailed = false;
        }
    }

    public async Task RetryReactionAsync(ChatMessage message)
    {
        if (string.IsNullOrEmpty(message.ReactionEmoji))
            return;

        string reactionText;
        try
        {
            reactionText = ReactionHelper.FormatReactionSms(message.ReactionEmoji, message.FullBody);
        }
        catch (ArgumentException)
        {
            // Stale unmapped emoji from before the dock was restricted: mark failed
            // and bail instead of throwing on a background tap handler.
            message.IsReactionSending = false;
            message.HasReactionFailed = true;
            return;
        }

        message.HasReactionFailed = false;
        message.IsReactionSending = true;

        var targetSubId = ResolveSubId(message.SubId);
        var address = SendAddress;

        _ = Task.Run(async () =>
        {
            try
            {
                var success = await _smsService.SendSmsAsync(address, reactionText, targetSubId);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    message.IsReactionSending = false;
                    message.HasReactionFailed = !success;
                });
            }
            catch
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    message.IsReactionSending = false;
                    message.HasReactionFailed = true;
                });
            }
        });
    }

    public async Task<bool> DeleteMessageAsync(ChatMessage message)
    {
        var success = await _smsService.DeleteMessagesAsync([message.Id]);
        if (success)
        {
            Items.Remove(message);
            Messages.Remove(message);
            lock (CacheLock)
            {
                RecentCache.Remove(Thread.ThreadId);
            }
        }
        return success;
    }

    public async Task<bool> DeleteSelectedMessagesAsync()
    {
        var selected = Messages.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
            return false;

        var ids = selected.Select(m => m.Id).ToList();
        var success = await _smsService.DeleteMessagesAsync(ids);
        if (success)
        {
            foreach (var msg in selected)
            {
                Items.Remove(msg);
                Messages.Remove(msg);
            }
            ExitSelectionMode();
            lock (CacheLock)
            {
                RecentCache.Remove(Thread.ThreadId);
            }
        }
        return success;
    }

    public string GetSelectedMessagesText()
    {
        var selected = Messages.Where(m => m.IsSelected).OrderBy(m => m.Timestamp).ToList();
        return string.Join(Environment.NewLine, selected.Select(m => m.FullBody));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
