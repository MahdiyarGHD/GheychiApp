using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.Services;
using Gheychi.Core.Folders;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public sealed class MessagesViewModel : INotifyPropertyChanged
{
    private readonly ISmsService _smsService;
    private readonly IDateFormattingService _dateFormatter;
    private readonly IDraftStore? _drafts;
    private readonly IPinStore? _pins;
    private readonly PreferencesFolderStore? _folders;
    private List<ThreadItem> _inboxAll = [];
    private string _activeFolderId = Folder.AllId;
    private int _barRefreshQueued;
    private bool _reordering;
    private BuiltThreads? _deferred;
    private bool _isLoading;
    private bool _hasPermission = true;
    private static readonly TimeSpan ReturnRefreshDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ReturnRefreshMinInterval = TimeSpan.FromSeconds(2);
    private bool _initialized;
    private DateTime _lastLoadedAt;
    private bool _reloadPending;
    private Task<BuiltThreads>? _snapshotTask;
    private readonly TaskCompletionSource _cachedInboxTried = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private const string ArchivedKey = "archived_threads_v1";
    private const int MaxIncrementalChanges = 40;
    private const int PreloadCount = 8;
    private bool _chatPreloadEnabled;

    private static string SnapshotPath => Path.Combine(FileSystem.AppDataDirectory, "threads_snapshot.json");

    public MessagesViewModel(ISmsService? smsService = null, IDateFormattingService? dateFormatter = null)
    {
        _smsService = smsService ?? IPlatformApplication.Current?.Services.GetService<ISmsService>() ?? throw new InvalidOperationException("ISmsService not resolved");
        _dateFormatter = dateFormatter ?? IPlatformApplication.Current?.Services.GetService<IDateFormattingService>() ?? new DateFormattingService();
        _drafts = IPlatformApplication.Current?.Services.GetService<IDraftStore>();
        _pins = IPlatformApplication.Current?.Services.GetService<IPinStore>();
        _folders = IPlatformApplication.Current?.Services.GetService<PreferencesFolderStore>();
        if (AppPreferences.RememberLastFolder)
            _activeFolderId = AppPreferences.LastFolderId is { Length: > 0 } last ? last : Folder.AllId;
        if (_folders is not null)
            _folders.Changed += () => MainThread.BeginInvokeOnMainThread(() => ApplyFolderView());
        ThreadItem.UnreadChanged += QueueFolderBarRefresh;

        // Normally read while the XAML is inflated. Opened from a notification, the inbox is not what is
        // waited for: the CPU belongs to the chat until it is up, so the read waits for InitializeAsync.
        if (!ChatLaunchRequests.HasPending)
            _snapshotTask = StartSnapshotLoad();
        else
            _cachedInboxTried.TrySetResult();

        SmsDeliverReceiver.SmsReceived += OnSmsReceived;
        NotificationActionReceiver.ThreadsChanged += OnSmsReceived;
        // A send result or delivery report often arrives after the chat was closed; its cached page would still show
        // the spinner or one tick.
        SmsSendTracker.OutgoingUpdated += (_, threadId, _) =>
        {
            if (threadId > 0)
                ChatViewModel.InvalidateThread(threadId);
        };
        SmsSendTracker.Delivered += (_, threadId) =>
        {
            if (threadId > 0)
                ChatViewModel.InvalidateThread(threadId);
        };
        SmsSendTracker.DeliveryFailed += (_, threadId) =>
        {
            if (threadId > 0)
                ChatViewModel.InvalidateThread(threadId);
        };
    }

    // ---- Folders --------------------------------------------------------------------------------

    /// <summary>The tabs above the inbox.</summary>
    public FolderBar FolderBar { get; } = new();

    public string ActiveFolderId => _activeFolderId;

    /// <summary>A folder with nothing in it right now: the list shows a hint instead of blank space.</summary>
    public bool IsFolderEmpty => Threads.Count == 0 && _inboxAll.Count > 0 && ActiveId != Folder.AllId;

    /// <summary>The folder the list shows: with the tabs hidden the inbox is the whole inbox, whatever folder was open.</summary>
    private string ActiveId => AppPreferences.ShowFolderBar ? _activeFolderId : Folder.AllId;

    /// <summary>A switch has started: the page fades the list out. Followed by <see cref="FolderSwitched"/>.</summary>
    public event Action? FolderSwitching;

    /// <summary>The list holds the new folder's conversations: the page brings it back.</summary>
    public event Action? FolderSwitched;

    private static readonly TimeSpan FolderFade = TimeSpan.FromMilliseconds(90);

    public void SelectFolder(string id)
    {
        if (id == _activeFolderId)
            return;

        _activeFolderId = id;
        if (AppPreferences.RememberLastFolder)
            AppPreferences.LastFolderId = id;

        if (IsSelectionMode)
            ExitSelectionMode();

        // The tab answers the tap at once; the list is swapped under a short fade, which is cheaper to look at than a jump.
        RefreshFolderBar();
        FolderSwitching?.Invoke();
        DispatcherTimer.RunOnce(() =>
        {
            ApplyFolderView();
            FolderSwitched?.Invoke();
        }, FolderFade);
    }

    /// <summary>Marks every conversation of the folder as read.</summary>
    public void MarkFolderRead(string folderId)
    {
        var folders = _folders?.Folders ?? [];
        foreach (var t in FolderItems(folderId, folders).Where(t => t.IsUnread).ToList())
        {
            t.MarkAsRead();
            var threadId = t.ThreadId;
            _ = Task.Run(() => _smsService.MarkThreadAsReadAsync(threadId));
        }

        RefreshFolderBar();
    }

    private void ShowInbox(List<ThreadItem> inbox)
    {
        _inboxAll = inbox;
        ApplyFolderView(fresh: true);
    }

    // The visible list follows the folder and the bar follows the list. Works on what the rows are now: a row opened
    // and read since the last read of the provider is read here too.
    private void ApplyFolderView(bool fresh = false)
    {
        var folders = _folders?.Folders ?? [];
        if (_activeFolderId != Folder.AllId && folders.All(f => f.Id != _activeFolderId))
            _activeFolderId = Folder.AllId;

        // Without folders, or with their tabs hidden, the list is what was read, in the order it was read.
        var filtered = folders.Count > 0 && AppPreferences.ShowFolderBar;
        ApplyItems(Threads, filtered ? FolderItems(_activeFolderId, folders, fresh) : _inboxAll);
        RefreshFolderBar();
        OnPropertyChanged(nameof(IsFolderEmpty));
    }

    // A list just read is the truth. Between reads (a folder opened, a setting changed) the rows on screen are, as they may
    // have been read since; taking them after a read would put back the preview and the unread state it just replaced.
    private List<ThreadItem> FolderItems(string folderId, IReadOnlyList<Folder> folders, bool fresh = false)
    {
        var live = Threads.ToDictionary(t => t.ThreadId);
        var pinOrder = _pins?.GetOrder() ?? [];
        var pinned = pinOrder.ToHashSet();
        var current = fresh ? _inboxAll.ToList() : _inboxAll.Select(t => live.GetValueOrDefault(t.ThreadId, t)).ToList();
        foreach (var t in current)
            t.IsPinned = pinned.Contains(t.ThreadId);

        var ordered = PinnedOrder.Apply(current.OrderByDescending(t => t.Timestamp).ToList(), pinOrder, t => t.ThreadId);
        if (folderId == Folder.AllId)
            return ordered.ToList();

        var folder = folders.FirstOrDefault(f => f.Id == folderId);
        if (folder is null)
            return ordered.ToList();

        var members = folder.IsSmart ? new HashSet<long>() : _folders!.Members(folder.Id);
        return ordered.Where(t => FolderRules.Matches(folder, FactsOf(t), members.Contains(t.ThreadId))).ToList();
    }

    private static ThreadFacts FactsOf(ThreadItem t) =>
        new(t.IsUnread, t.IsContact, t.IsSender, t.HasCode, t.HasFailed, t.HasDraft, t.IsPinned);

    // Reading and unreading come in bursts (an inbox being built, a selection marked): one refresh at the end of them.
    private void QueueFolderBarRefresh()
    {
        if (Interlocked.Exchange(ref _barRefreshQueued, 1) == 1)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Interlocked.Exchange(ref _barRefreshQueued, 0);
            RefreshFolderBar();
        });
    }

    private void RefreshFolderBar()
    {
        var folders = _folders?.Folders ?? [];
        var unread = new Dictionary<string, int>();
        if (folders.Count > 0)
        {
            var live = Threads.ToDictionary(t => t.ThreadId);
            var current = _inboxAll.Select(t => live.GetValueOrDefault(t.ThreadId, t)).ToList();
            unread[Folder.AllId] = current.Count(t => t.IsUnread);
            foreach (var folder in folders)
            {
                var members = folder.IsSmart ? new HashSet<long>() : _folders!.Members(folder.Id);
                unread[folder.Id] = current.Count(t => t.IsUnread && FolderRules.Matches(folder, FactsOf(t), members.Contains(t.ThreadId)));
            }
        }

        FolderBar.Show(folders, _activeFolderId, unread, AppPreferences.ShowFolderCounts, AppPreferences.ShowFolderBar);
    }

    /// <summary>Redraws the tabs after the settings that shape them changed.</summary>
    public void RefreshFolders() => ApplyFolderView();

    private Task<BuiltThreads> StartSnapshotLoad()
    {
        var snapshotPath = SnapshotPath;
        return Task.Run(() => BuildItems(ThreadSnapshotStore.TryLoad(snapshotPath)));
    }

    public FastObservableCollection<ThreadItem> Threads { get; } = [];

    public FastObservableCollection<ThreadItem> ArchivedThreads { get; } = [];

    private readonly record struct BuiltThreads(List<ThreadItem> Inbox, List<ThreadItem> Archived, int PinVersion);

    private bool _isSelectionMode;
    public bool IsSelectionMode
    {
        get => _isSelectionMode;
        private set
        {
            if (SetField(ref _isSelectionMode, value))
            {
                OnPropertyChanged(nameof(IsNotSelectionMode));
            }
        }
    }

    public bool IsNotSelectionMode => !IsSelectionMode;

    public int SelectedCount => Threads.Count(t => t.IsSelected);
    public string SelectedCountText => SelectedCount.ToString();
    public IReadOnlyList<ThreadItem> SelectedThreads => Threads.Where(t => t.IsSelected).ToList();
    public bool AnySelectedUnread => SelectedThreads.Any(t => t.IsUnread);

    public bool AnySelectedRead => SelectedThreads.Any(t => !t.IsUnread);

    public bool AnySelectedPinned => SelectedThreads.Any(t => t.IsPinned);

    public bool AnySelectedNotPinned => SelectedThreads.Any(t => !t.IsPinned);

    /// <summary>How many rows are pinned: they are always the first ones of the list.</summary>
    public int PinnedCount => Threads.TakeWhile(t => t.IsPinned).Count();

    public void EnterSelectionMode(ThreadItem initial)
    {
        initial.IsSelected = true;
        IsSelectionMode = true;
        UpdateSelectedCount();
    }

    public void ToggleThreadSelection(ThreadItem thread)
    {
        thread.IsSelected = !thread.IsSelected;
        UpdateSelectedCount();
        if (SelectedCount == 0)
        {
            ExitSelectionMode();
        }
    }

    public void ExitSelectionMode()
    {
        foreach (var t in Threads)
        {
            t.IsSelected = false;
        }
        IsSelectionMode = false;
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SelectedThreads));
        OnPropertyChanged(nameof(AnySelectedUnread));
        OnPropertyChanged(nameof(AnySelectedRead));
        OnPropertyChanged(nameof(AnySelectedPinned));
        OnPropertyChanged(nameof(AnySelectedNotPinned));
    }

    public Task<bool> UnarchiveThreadsAsync(IReadOnlyList<ThreadItem> threads)
    {
        if (threads.Count == 0)
            return Task.FromResult(false);

        var archivedIds = GetArchivedThreadIds();
        foreach (var t in threads)
            archivedIds.Remove(t.ThreadId);
        SaveArchivedThreadIds(archivedIds);

        // The rows leave the archive now; the reload puts them back into the inbox in date order.
        foreach (var t in threads)
        {
            t.IsSelected = false;
            ArchivedThreads.Remove(t);
        }

        _ = LoadThreadsAsync();
        return Task.FromResult(true);
    }

    public async Task<bool> DeleteSelectedThreadsAsync()
    {
        var selected = SelectedThreads;
        if (selected.Count == 0)
            return false;

        var threadIds = selected.Select(t => t.ThreadId).ToList();
        var success = await _smsService.DeleteThreadsAsync(threadIds);
        if (success)
        {
            _drafts?.Remove(threadIds);
            _pins?.Unpin(threadIds);
        }

        // A failed delete (not the default SMS app, provider error) must not look like it worked:
        // the threads would vanish here and come back on the next reload.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (success)
            {
                foreach (var t in selected)
                {
                    Threads.Remove(t);
                }
            }
            ExitSelectionMode();
        });

        // Rewrites the snapshot too, so a cold start does not paint the deleted threads first.
        _ = LoadThreadsAsync();

        return success;
    }

    /// <summary>Moves one conversation to the archive, e.g. from its profile page.</summary>
    public Task<bool> ArchiveThreadAsync(ThreadItem thread)
    {
        var archivedIds = GetArchivedThreadIds();
        archivedIds.Add(thread.ThreadId);
        SaveArchivedThreadIds(archivedIds);
        // An archived conversation is not pinned any more.
        _pins?.Unpin([thread.ThreadId]);

        // The row moves to the archive straight away; the reload rebuilds both lists.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var row = Threads.FirstOrDefault(t => t.ThreadId == thread.ThreadId);
            if (row is not null)
            {
                row.IsPinned = false;
                Threads.Remove(row);
                ArchivedThreads.Add(row);
            }

            _ = LoadThreadsAsync();
        });

        return Task.FromResult(true);
    }

    public Task<bool> ArchiveSelectedThreadsAsync()
    {
        var selected = SelectedThreads;
        if (selected.Count == 0)
            return Task.FromResult(false);

        var archivedIds = GetArchivedThreadIds();
        foreach (var t in selected)
        {
            archivedIds.Add(t.ThreadId);
        }
        SaveArchivedThreadIds(archivedIds);
        _pins?.Unpin(selected.Select(t => t.ThreadId).ToList());

        // The moved rows are shown in the archive straight away; the reload below rebuilds both lists.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var t in selected)
            {
                Threads.Remove(t);
                t.IsSelected = false;
                t.IsPinned = false;
            }
            ExitSelectionMode();
            ArchivedThreads.AddRange(selected);
            _ = LoadThreadsAsync();
        });

        return Task.FromResult(true);
    }

    public Task PinSelectedAsync()
    {
        if (_pins is null)
            return Task.CompletedTask;

        // In the order they are listed, so pinning several keeps them as they were.
        var ids = Threads.Where(t => t.IsSelected && !t.IsPinned).Select(t => t.ThreadId).ToList();
        if (ids.Count > 0)
        {
            _pins.PinToTop(ids);
            ApplyPinOrder();
        }

        ExitSelectionMode();
        return Task.CompletedTask;
    }

    public Task UnpinSelectedAsync()
    {
        if (_pins is null)
            return Task.CompletedTask;

        var ids = Threads.Where(t => t.IsSelected && t.IsPinned).Select(t => t.ThreadId).ToList();
        if (ids.Count > 0)
        {
            _pins.Unpin(ids);
            ApplyPinOrder();
        }

        ExitSelectionMode();
        return Task.CompletedTask;
    }

    // Brings the list in line with the pins without reading anything: the pinned rows go to the top in their order, the
    // others back to where their date puts them. Only the rows that are out of place move.
    private void ApplyPinOrder()
    {
        var order = _pins?.GetOrder() ?? [];
        var pinned = order.ToHashSet();
        foreach (var t in Threads)
            t.IsPinned = pinned.Contains(t.ThreadId);

        var byDate = Threads.OrderByDescending(t => t.Timestamp).ToList();
        var wanted = PinnedOrder.Apply(byDate, order, t => t.ThreadId);
        for (var index = 0; index < wanted.Count; index++)
        {
            if (ReferenceEquals(Threads[index], wanted[index]))
                continue;

            Threads.Move(Threads.IndexOf(wanted[index]), index);
        }
    }

    /// <summary>Held while a pinned row is being dragged: what is read from the provider meanwhile waits for the drop.</summary>
    public void BeginReorder() => _reordering = true;

    public void EndReorder()
    {
        _reordering = false;
        if (_deferred is { } waiting)
        {
            _deferred = null;
            ShowBuilt(waiting);
        }
    }

    /// <summary>A pinned row dropped into another slot among the pinned rows.</summary>
    public void MovePinned(int from, int to)
    {
        if (_pins is null || from == to || from < 0 || to < 0 || from >= Threads.Count || to >= Threads.Count)
            return;

        if (!Threads[from].IsPinned || !Threads[to].IsPinned)
            return;

        Threads.Move(from, to);
        _pins.SetOrder(Threads.TakeWhile(t => t.IsPinned).Select(t => t.ThreadId).ToList());
    }

    public Task MarkSelectedAsReadAsync() => MarkSelectedAsync(unread: false);

    public Task MarkSelectedAsUnreadAsync() => MarkSelectedAsync(unread: true);

    private Task MarkSelectedAsync(bool unread)
    {
        var selected = SelectedThreads;
        if (selected.Count == 0)
            return Task.CompletedTask;

        foreach (var t in selected)
        {
            // Already in the wanted state: nothing to write, and no notification to dismiss.
            if (t.IsUnread == unread)
                continue;

            var threadId = t.ThreadId;
            if (unread)
            {
                t.MarkAsUnread();
                _ = Task.Run(() => _smsService.MarkThreadAsUnreadAsync(threadId));
            }
            else
            {
                t.MarkAsRead();
                _ = Task.Run(() => _smsService.MarkThreadAsReadAsync(threadId));
            }
        }

        ExitSelectionMode();
        return Task.CompletedTask;
    }

    private static HashSet<long> GetArchivedThreadIds()
    {
        try
        {
            var raw = Preferences.Default.Get<string>(ArchivedKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                return [];

            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => long.TryParse(s, out var id) ? id : -1)
                      .Where(id => id > 0)
                      .ToHashSet();
        }
        catch
        {
            return [];
        }
    }

    private static void SaveArchivedThreadIds(HashSet<long> ids)
    {
        try
        {
            var raw = string.Join(",", ids);
            Preferences.Default.Set(ArchivedKey, raw);
        }
        catch
        {
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool ChatPreloadEnabled => _chatPreloadEnabled;

    /// <summary>Starts reading the newest chats ahead so opening them is instant; called once the screens are built.</summary>
    public void EnableChatPreload()
    {
        _chatPreloadEnabled = true;
        StartChatPreload();
    }

    /// <summary>The threads the list is showing, or about to show; the page sets it. Without it the newest ones are read.</summary>
    public Func<List<ThreadItem>?>? PreloadTargets { get; set; }

    /// <summary>Reads the first page of the threads on screen ahead; call on the UI thread.</summary>
    public void StartChatPreload()
    {
        if (!_chatPreloadEnabled || Threads.Count == 0)
            return;

        var targets = PreloadTargets?.Invoke() ?? Threads.Take(PreloadCount).ToList();
        ChatViewModel.PreloadVisibleThreads(targets, _smsService, _dateFormatter, Math.Max(PreloadCount, targets.Count));
    }

    public bool HasPermission
    {
        get => _hasPermission;
        private set => SetField(ref _hasPermission, value);
    }

    /// <summary>
    /// Done once the last known inbox is on the list, or there is none. The launch screen stays up until then (for a
    /// moment at most), so the first frame is the inbox and not an empty list that fills a moment later.
    /// </summary>
    public Task CachedInboxTried => _cachedInboxTried.Task;

    /// <summary>The last known inbox, when the list is still empty: enough to know the names of the conversations.</summary>
    public async Task EnsureCachedInboxAsync()
    {
        try
        {
            if (Threads.Count > 0)
                return;

            var cached = await (_snapshotTask ??= StartSnapshotLoad());
            if (cached.Inbox.Count > 0 && Threads.Count == 0)
            {
                ShowInbox(cached.Inbox);
                if (ArchivedThreads.Count == 0)
                    ArchivedThreads.Reset(cached.Archived);
            }
        }
        finally
        {
            _cachedInboxTried.TrySetResult();
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            // Back on the inbox (tab switch, chat closed, app resumed): the list is only refreshed
            // by incoming SMS otherwise, so sent replies, read state and "Yesterday" labels go stale.
            // Prompts are not repeated; they ran once and re-launching the role dialog on every
            // return to the tab is worse than an empty list.
            _ = RefreshAfterReturnAsync();
            return;
        }

        _initialized = true;
        if (IPlatformApplication.Current?.Services.GetService<ITrashRepository>() is { } trash)
            _ = Task.Run(() => trash.DeleteOlderThanAsync(TrashPolicy.Cutoff(DateTime.Now)));

        // Paint the last known inbox right away; the real query below refreshes it.
        await EnsureCachedInboxAsync();

        await _smsService.EnsureDefaultSmsAppAsync();
        var granted = await _smsService.EnsurePermissionsAsync();
        HasPermission = granted;
        await LoadThreadsAsync();
    }

    // The reload waits for the tab or chat transition to finish, and is skipped when the list was just read.
    private async Task RefreshAfterReturnAsync()
    {
        await Task.Delay(ReturnRefreshDelay);
        if (DateTime.UtcNow - _lastLoadedAt > ReturnRefreshMinInterval)
            await LoadThreadsAsync();
    }

    public async Task LoadThreadsAsync()
    {
        if (IsLoading)
        {
            _reloadPending = true;
            return;
        }

        IsLoading = true;
        try
        {
            var rawThreads = await _smsService.GetThreadsAsync();

            // The provider errors are swallowed into an empty list. Applying it would wipe a
            // populated inbox row by row and overwrite the snapshot with nothing.
            if (rawThreads.Count == 0 && Threads.Count > 0)
                return;

            var items = await Task.Run(() => BuildItems(rawThreads));

            MainThread.BeginInvokeOnMainThread(() => ShowBuilt(items));

            if (HasPermission)
                _ = Task.Run(() => ThreadSnapshotStore.Save(SnapshotPath, rawThreads));

            // Before the screens are warmed up the CPU belongs to them; EnableChatPreload starts this later.
            if (_chatPreloadEnabled)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(800);
                    MainThread.BeginInvokeOnMainThread(StartChatPreload);
                });
            }
        }
        finally
        {
            IsLoading = false;
            _lastLoadedAt = DateTime.UtcNow;
            if (_reloadPending)
            {
                _reloadPending = false;
                _ = LoadThreadsAsync();
            }
        }
    }

    // On the UI thread. A list built before the pins changed would undo the change on screen.
    private void ShowBuilt(BuiltThreads built)
    {
        if (_reordering)
        {
            _deferred = built;
            return;
        }

        if ((_pins?.Version ?? 0) != built.PinVersion)
        {
            _ = LoadThreadsAsync();
            return;
        }

        ShowInbox(built.Inbox);
        ApplyItems(ArchivedThreads, built.Archived);
    }

    private BuiltThreads BuildItems(IReadOnlyList<SmsThread> rawThreads)
    {
        var pinVersion = _pins?.Version ?? 0;
        var pinOrder = _pins?.GetOrder() ?? [];
        var pinned = pinOrder.ToHashSet();
        var archivedIds = GetArchivedThreadIds();
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentUICulture;
        var draftPreview = Gheychi.App.Localization.LocalizationManager.Instance["Inbox_DraftPreview"];
        var findCodes = _folders?.Has(SmartFolder.Codes) == true;
        rawThreads = DraftInbox.Merge(rawThreads, _drafts?.GetAll() ?? []);

        var items = new List<ThreadItem>(rawThreads.Count);
        var archived = new List<ThreadItem>();
        foreach (var t in rawThreads)
        {
            var isSender = PhoneNumberNormalizer.IsAlphanumeric(t.Address);
            var name = !string.IsNullOrWhiteSpace(t.ContactName)
                ? t.ContactName
                : isSender
                    ? t.Address
                    : PhoneNumberNormalizer.FormatDisplay(t.Address);

            var isArchived = archivedIds.Contains(t.ThreadId);
            (isArchived ? archived : items).Add(new ThreadItem
            {
                ThreadId = t.ThreadId,
                SubId = t.SubId,
                Name = name,
                Phone = PhoneNumberNormalizer.FormatDisplay(t.Address),
                Initials = ThreadItem.GenerateInitials(name),
                IconFile = ThreadItem.DetectIcon(name, t.Address),
                Count = t.UnreadCount,
                TotalCount = t.TotalCount,
                Time = _dateFormatter.FormatThreadTime(t.Timestamp, now, culture),
                Preview = t.HasDraft ? string.Format(draftPreview, t.Snippet) : t.Snippet,
                HasDraft = t.HasDraft,
                Timestamp = t.Timestamp,
                IsPinned = !isArchived && pinned.Contains(t.ThreadId),
                IsUnread = t.UnreadCount > 0,
                HasFailed = t.HasFailed,
                // The thread reader fills ContactName with the sender id itself for alphanumeric senders, so the
                // name alone does not make a contact: only a phone number with a saved name does.
                IsContact = !isSender && !string.IsNullOrWhiteSpace(t.ContactName),
                IsSender = isSender,
                HasCode = findCodes && OtpDetector.Find(t.Snippet) is not null
            });
        }

        var ordered = PinnedOrder.Apply(items, pinOrder, t => t.ThreadId);
        return new BuiltThreads(ordered as List<ThreadItem> ?? ordered.ToList(), archived, pinVersion);
    }

    // A full Reset drops the scroll position and rebinds every visible row, so refreshes
    // after the first paint only touch the rows that actually changed.
    internal static void ApplyItems(FastObservableCollection<ThreadItem> target, List<ThreadItem> items)
    {
        if (target.Count == 0)
        {
            if (items.Count > 0)
                target.Reset(items);
            return;
        }

        // Individual notifications are cheaper than a Reset only while few rows change.
        if (ListSynchronizer.ExceedsChangeLimit(target, items, t => t.ThreadId, (a, b) => a.HasSameContent(b), MaxIncrementalChanges))
        {
            var selectedIds = target.Where(t => t.IsSelected).Select(t => t.ThreadId).ToHashSet();
            foreach (var item in items)
                item.IsSelected = selectedIds.Contains(item.ThreadId);
            target.Reset(items);
            return;
        }

        ListSynchronizer.Sync(
            target,
            items,
            t => t.ThreadId,
            (a, b) => a.HasSameContent(b),
            (old, replacement) => replacement.IsSelected = old.IsSelected);
    }

    // threadId 0: the conversation is unknown, so every cached page may be stale.
    private void OnSmsReceived(long threadId)
    {
        if (threadId > 0)
            ChatViewModel.InvalidateThread(threadId);
        else
            ChatViewModel.InvalidateCache();

        _ = LoadThreadsAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
