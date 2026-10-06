using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Gheychi.App.Gestures;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.Core.Models;
using Gheychi.Core.Notifications;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public sealed record PreparedChatData(
    List<object> Items,
    List<ChatMessage> Messages,
    string StickyDate,
    int SimSlot,
    int? FirstUnreadIndex = null,
    int RawCount = 0
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
    private readonly IThreadSettings? _threadSettings;
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

        _threadSettings = IPlatformApplication.Current?.Services.GetService<IThreadSettings>();

        _thread = thread ?? ThreadItem.Empty;
        Items = [];
        Messages = [];

        Search = new ChatSearchState(_smsService);
        Search.Changed += RefreshSearchMarks;
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatSearchState.IsActive))
                OnPropertyChanged(nameof(IsInputBarVisible));
        };

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

    public ChatSearchState Search { get; }

    // Outlines the matching bubbles. A setter that finds the same mark does nothing, so this is cheap to repeat.
    private void RefreshSearchMarks()
    {
        foreach (var message in Messages)
            message.SearchMark = Search.MarkFor(message.Id);
    }

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
                    await Task.Delay(250, ct);

                    // Reading a chat is real work (provider, SQLite, allocations): not while the user is touching the screen.
                    await UserActivity.WaitForIdleAsync(500, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    var generation = CurrentCacheGeneration();
                    var loader = new ChatViewModel(null, smsService, dateFormatter);
                    var data = await loader.FetchMessagesAsync(thread, RecentPageSize, cancellationToken: ct);

                    // Empty means the provider failed (a thread always has messages); caching it
                    // would open that chat blank. A bumped generation means a message arrived
                    // while this was being read, so the page is already stale.
                    if (!ct.IsCancellationRequested && data.RawCount > 0 && generation == CurrentCacheGeneration())
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

    /// <summary>
    /// Starts reading a notified conversation the moment its tap arrives, in parallel with the app starting up
    /// or resuming, so the chat opens with its messages already cached. Returns the conversation's unread count.
    /// </summary>
    public static Task<int> PrepareLaunchAsync(ChatLaunchRequest request) =>
        Task.Run(async () =>
        {
            var unread = ConversationReader.CountUnread(Microsoft.Maui.ApplicationModel.Platform.AppContext, request.ThreadId);

            var services = IPlatformApplication.Current?.Services;
            var smsService = services?.GetService<ISmsService>();
            if (smsService is null)
                return unread;

            try
            {
                var dateFormatter = services!.GetService<IDateFormattingService>() ?? new DateFormattingService();
                var generation = CurrentCacheGeneration();
                var thread = ThreadItem.ForConversation(request.ThreadId, request.SubId, request.Name, request.Address);
                var loader = new ChatViewModel(null, smsService, dateFormatter);

                // The hint widens the page when more is unread than one page holds, so the divider is found.
                var data = await loader.FetchMessagesAsync(thread, RecentPageSize, unreadHint: unread);
                if (data.RawCount > 0 && generation == CurrentCacheGeneration())
                    AddToCache(request.ThreadId, data);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Prepare notified chat failed: {ex}");
            }

            return unread;
        });

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

    private static int _cacheGeneration;

    private static int CurrentCacheGeneration()
    {
        lock (CacheLock)
        {
            return _cacheGeneration;
        }
    }

    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            _cacheGeneration++;
            RecentCache.Clear();
            CacheOrder.Clear();
        }
    }

    /// <summary>
    /// A message arrived in, or an action changed, one conversation: its cached page is stale, the others are not.
    /// The generation still moves, so a read-ahead that was running during the change is not cached.
    /// </summary>
    public static void InvalidateThread(long threadId)
    {
        lock (CacheLock)
        {
            _cacheGeneration++;
            RecentCache.Remove(threadId);
            CacheOrder.Remove(threadId);
        }
    }

    /// <summary>Drops one thread's cached page (e.g. after it was marked read, so the unread divider is not replayed).</summary>
    public static void EvictCache(long threadId)
    {
        lock (CacheLock)
        {
            RecentCache.Remove(threadId);
            CacheOrder.Remove(threadId);
        }
    }

    // The day header the row sits under. Read from the list itself, so rows added, removed or
    // prepended since the page was built can never leave it pointing at the wrong day.
    public string GetDateForIndex(int firstVisibleIndex)
    {
        for (var i = Math.Min(firstVisibleIndex, Items.Count - 1); i >= 0; i--)
        {
            if (Items[i] is DateSeparatorItem separator)
                return separator.Text;
        }

        return StickyDate;
    }

    public async Task<PreparedChatData> FetchMessagesAsync(ThreadItem thread, int? limit = RecentPageSize, int offset = 0, CancellationToken cancellationToken = default, int unreadHint = 0)
    {
        await EnsureSimInfoLoadedAsync();
        var isDual = _isDualSim ?? false;
        var slotMap = _simSlotMap;
        var carrierMap = _simCarrierMap;

        // The inbox row is marked read before the fetch runs, so the unread count it had when
        // tapped comes in as a hint.
        var unread = Math.Max(unreadHint, thread.IsUnread ? thread.Count : 0);
        var fetchLimit = limit;
        if (offset == 0 && unread > (limit ?? RecentPageSize))
        {
            fetchLimit = Math.Min(unread + 10, 100);
        }

        var rawMessages = await _smsService.GetMessagesAsync(thread.ThreadId, fetchLimit, offset, cancellationToken);
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentUICulture;

        DateTime? lastDate = null;
        var newItems = new List<object>(rawMessages.Count * 2);
        var newMessages = new List<ChatMessage>(rawMessages.Count);
        int? firstUnreadIndex = null;
        var unreadInserted = false;

        foreach (var msg in rawMessages)
        {
            if (lastDate == null || msg.Timestamp.Date != lastDate.Value.Date)
            {
                newItems.Add(new DateSeparatorItem(_dateFormatter.FormatDateSeparator(msg.Timestamp, now, culture)));
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

        // Reaction SMS are folded away, so paging has to count the provider rows that were read.
        return new PreparedChatData(newItems, newMessages, sticky, threadSlot, firstUnreadIndex, SmsMessagePage.RawCountOf(rawMessages));
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
        ApplyPreferredSim();
        if (Search.IsActive)
            RefreshSearchMarks();

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
            ApplyMessages(cached, cached.RawCount);
            return;
        }

        var data = await FetchMessagesAsync(thread);
        ApplyMessages(data, data.RawCount);
    }

    /// <summary>
    /// Loads older history until the message <paramref name="row"/> rows from the newest is in the list (search jumps to
    /// messages that were never scrolled to). The gap is read in one query and prepended once. False if the history ends first.
    /// </summary>
    public async Task<bool> EnsureLoadedThroughAsync(int row)
    {
        for (var attempt = 0; row >= _loadedCount && attempt < 50; attempt++)
        {
            if (_loadingOlder)
            {
                // A scroll-triggered page is in flight; its result changes what is still missing.
                await Task.Delay(40);
                continue;
            }

            if (!_hasMore)
                return false;

            await LoadOlderAsync(minRows: row - _loadedCount + 1);
        }

        return row < _loadedCount;
    }

    public async Task<bool> LoadOlderAsync(int minRows = 0)
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
                    page = (_prefetchThreadId == Thread.ThreadId && pendingOffset == _loadedCount && minRows <= RecentPageSize)
                        ? await pending
                        : null;
                }
                catch
                {
                    page = null;
                }
            }

            if (page is null)
            {
                var current = Thread;
                var offset = _loadedCount;
                var limit = Math.Max(RecentPageSize, minRows);
                page = await Task.Run(() => FetchMessagesAsync(current, limit, offset));
            }

            if (page == null || page.RawCount == 0)
            {
                _hasMore = false;
                return false;
            }

            var count = page.RawCount;
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

        // A day split across two pages: the page ends with that day's messages and the list
        // already starts with the same day's separator. Keep the page's (it sits above the day's
        // first message) and drop the list's, otherwise the day shows two headers. Compared by
        // date: the header text carries the time of the day's first row, so it differs per page.
        var dropHead = page.Messages.Count > 0 && Messages.Count > 0 &&
            Items.Count > 0 && Items[0] is DateSeparatorItem &&
            page.Messages[^1].Timestamp.Date == Messages[0].Timestamp.Date;
        if (dropHead)
            Items.RemoveAt(0);

        // Incremental prepend: single range-insert notification so the
        // RecyclerView shifts existing rows instead of rebinding everything.
        Items.PrependRange(page.Items);
        Messages.PrependRange(page.Messages);
        if (Search.IsActive)
            RefreshSearchMarks();

        EvictCache(Thread.ThreadId);
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

    /// <summary>A SIM the user chose for this conversation wins over the one its last message used, while that SIM is still in the phone.</summary>
    private void ApplyPreferredSim()
    {
        var preferred = _threadSettings?.GetPreferredSubId(Thread.ThreadId) ?? 0;
        if (preferred <= 0)
            return;

        var sim = ActiveSims.FirstOrDefault(s => s.SubId == preferred);
        if (sim is not null)
            SelectSim(sim.SlotIndex, sim.SubId);
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
            IsDualSim = _isDualSim ?? false,
            Timestamp = DateTime.Now
        };

        AppendMessage(outgoingMsg);

        EvictCache(Thread.ThreadId);

        await DeliverAsync(outgoingMsg, text, ResolveSubId(0), storeFirst: true);
    }

    public async void Retry(ChatMessage message)
    {
        message.HasFailed = false;
        message.IsDelivered = false;
        var text = string.IsNullOrEmpty(message.Link) ? message.BodyBeforeLink : $"{message.BodyBeforeLink}{message.Link}";

        // The message's own SIM, not whichever SIM is selected now.
        await DeliverAsync(message, text, ResolveSubId(message.SubId), storeFirst: message.Id <= 0);
    }

    // Shared by Send and Retry. The message is stored as outgoing before it is sent, so a killed
    // process or a slow radio leaves it in the history instead of losing it; a retry reuses the
    // stored row instead of adding a second one.
    private async Task DeliverAsync(ChatMessage message, string text, int subId, bool storeFirst)
    {
        // async void callers: an exception escaping here would kill the process.
        var result = SmsSendResult.Failed;
        try
        {
            var address = SendAddress;
            if (storeFirst)
            {
                var rowId = await _smsService.QueueOutgoingAsync(address, text, subId);
                if (rowId > 0)
                {
                    message.Id = rowId;
                    // A new newest row: every later page offset moves down by one.
                    ShiftLoadedCount(1);
                }
            }

            result = await _smsService.SendSmsAsync(address, text, subId, message.Id);
        }
        catch (Exception)
        {
        }

        EvictCache(Thread.ThreadId);

        // Pending: no answer yet, the bubble stays "sending" and the final result arrives through
        // OutgoingUpdated when the radio reports (or the stored state is picked up on reopen).
        switch (result)
        {
            case SmsSendResult.Sent:
                message.IsDelivered = true;
                break;
            case SmsSendResult.Failed:
                message.HasFailed = true;
                break;
        }
    }

    private void OnOutgoingUpdated(long rowId, bool ok)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var message in Messages)
            {
                if (message.Id != rowId)
                    continue;

                message.IsDelivered = ok;
                message.HasFailed = !ok;
                break;
            }
        });
    }

    // ---- Live updates while the chat is open --------------------------------------------------

    private bool _live;
    private bool _refreshing;
    private bool _refreshAgain;

    /// <summary>Raised after messages were appended to the open chat (so the view can follow them).</summary>
    public event Action? MessagesAppended;

    public void StartLiveUpdates()
    {
        if (_live)
            return;

        _live = true;
        SmsDeliverReceiver.SmsReceived += OnIncomingSms;
        SmsSendTracker.OutgoingUpdated += OnOutgoingUpdated;
    }

    public void StopLiveUpdates()
    {
        if (!_live)
            return;

        _live = false;
        SmsDeliverReceiver.SmsReceived -= OnIncomingSms;
        SmsSendTracker.OutgoingUpdated -= OnOutgoingUpdated;
    }

    private void OnIncomingSms(long threadId)
    {
        // A message for another conversation changes nothing in this one.
        if (threadId > 0 && threadId != Thread.ThreadId)
            return;

        _ = RefreshNewestAsync();
    }

    private async Task RefreshNewestAsync()
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                var thread = Thread;
                if (thread.ThreadId <= 0)
                    return;

                // Building the page (links, dates, rows) must not run on the UI thread, where this was called from.
                var data = await Task.Run(() => FetchMessagesAsync(thread, RecentPageSize, 0));
                await MainThread.InvokeOnMainThreadAsync(() => AppendNew(thread.ThreadId, data));
            }
            while (_refreshAgain);
        }
        catch (Exception)
        {
            // Best effort: the next incoming message or reopening the chat catches up.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void AppendNew(long threadId, PreparedChatData data)
    {
        // Another chat was opened while this was being read, or the first page is still loading.
        if (Thread.ThreadId != threadId || Messages.Count == 0)
            return;

        var known = new Dictionary<long, ChatMessage>();
        foreach (var existing in Messages)
        {
            if (existing.Id > 0)
                known[existing.Id] = existing;
        }

        // A send whose row is not stored yet (Id 0) would show up here as a "new" outgoing message.
        var sendInFlight = Messages.Any(m => m.Id == 0 && m.IsOutgoing);

        var appended = 0;
        ChatMessage? lastIncoming = null;

        foreach (var fetched in data.Messages)
        {
            if (known.TryGetValue(fetched.Id, out var current))
            {
                if (current.ReactionEmoji != fetched.ReactionEmoji && !current.IsReactionSending)
                    current.ReactionEmoji = fetched.ReactionEmoji;
                continue;
            }

            if (sendInFlight && fetched.IsOutgoing)
                continue;

            // The chat is open and on screen: what arrives is read.
            fetched.IsUnread = false;
            AppendMessage(fetched);
            appended++;
            if (!fetched.IsOutgoing)
                lastIncoming = fetched;
        }

        if (appended == 0)
            return;

        if (lastIncoming != null)
        {
            foreach (var message in Messages)
                message.IsLastMessage = false;
            lastIncoming.IsLastMessage = true;
        }

        ShiftLoadedCount(appended);
        EvictCache(threadId);
        MessagesAppended?.Invoke();
        _ = _smsService.MarkThreadAsReadAsync(threadId);
    }

    // Adds a message at the bottom, under a new day header when it is the first message of its day.
    // Decided against the last message on screen, not the fetched page: a sent bubble is shown before
    // its row is stored, so the page cannot tell which days the list already has a header for.
    private void AppendMessage(ChatMessage message)
    {
        var previous = Messages.Count > 0 ? Messages[^1] : null;
        if (previous is null || previous.Timestamp.Date != message.Timestamp.Date)
        {
            var text = _dateFormatter.FormatDateSeparator(message.Timestamp, DateTime.Now, CultureInfo.CurrentUICulture);
            Items.Add(new DateSeparatorItem(text));
        }

        Items.Add(message);
        Messages.Add(message);
    }

    // A day whose messages were all deleted keeps no header.
    private void RemoveEmptyDaySeparators()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i] is DateSeparatorItem && (i == Items.Count - 1 || Items[i + 1] is DateSeparatorItem))
                Items.RemoveAt(i);
        }
    }

    // Rows stored or deleted behind the paging cursor shift every later OFFSET. Without this the
    // next older page repeats a message (insert) or skips some (delete), and a page fetched ahead
    // for the old offset is wrong.
    private void ShiftLoadedCount(int delta)
    {
        _loadedCount = Math.Max(0, _loadedCount + delta);
        try { _prefetchCts?.Cancel(); } catch { }
        _prefetchTask = null;
        _prefetchOffset = -1;
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
                OnPropertyChanged(nameof(IsInputBarVisible));
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

    // The message box makes no sense while picking messages or while the search bar and its keyboard are up.
    public bool IsInputBarVisible => !IsSelectionMode && !Search.IsActive;

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
                    var result = await _smsService.SendSmsAsync(address, reactionText, targetSubId);
                    ShiftLoadedCount(1);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        message.IsReactionSending = false;
                        message.HasReactionFailed = result == SmsSendResult.Failed;
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
                var result = await _smsService.SendSmsAsync(address, reactionText, targetSubId);
                ShiftLoadedCount(1);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    message.IsReactionSending = false;
                    message.HasReactionFailed = result == SmsSendResult.Failed;
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
            RemoveEmptyDaySeparators();
            ShiftLoadedCount(-1);
            EvictCache(Thread.ThreadId);
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
            RemoveEmptyDaySeparators();
            ExitSelectionMode();
            ShiftLoadedCount(-selected.Count);
            EvictCache(Thread.ThreadId);
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
