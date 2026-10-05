using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public sealed class MessagesViewModel : INotifyPropertyChanged
{
    private readonly ISmsService _smsService;
    private readonly IDateFormattingService _dateFormatter;
    private bool _isLoading;
    private bool _hasPermission = true;
    private bool _initialized;
    private bool _reloadPending;
    private Task<BuiltThreads>? _snapshotTask;

    private const string ArchivedKey = "archived_threads_v1";
    private const int MaxIncrementalChanges = 40;
    private const int PreloadCount = 8;
    private bool _chatPreloadEnabled;

    private static string SnapshotPath => Path.Combine(FileSystem.AppDataDirectory, "threads_snapshot.json");

    public MessagesViewModel(ISmsService? smsService = null, IDateFormattingService? dateFormatter = null)
    {
        _smsService = smsService ?? IPlatformApplication.Current?.Services.GetService<ISmsService>() ?? throw new InvalidOperationException("ISmsService not resolved");
        _dateFormatter = dateFormatter ?? IPlatformApplication.Current?.Services.GetService<IDateFormattingService>() ?? new DateFormattingService();

        // Normally read while the XAML is inflated. Opened from a notification, the inbox is not what is
        // waited for: the CPU belongs to the chat until it is up, so the read waits for InitializeAsync.
        if (!ChatLaunchRequests.HasPending)
            _snapshotTask = StartSnapshotLoad();

        SmsDeliverReceiver.SmsReceived += OnSmsReceived;
        NotificationActionReceiver.ThreadsChanged += OnSmsReceived;
    }

    private Task<BuiltThreads> StartSnapshotLoad()
    {
        var snapshotPath = SnapshotPath;
        return Task.Run(() => BuildItems(ThreadSnapshotStore.TryLoad(snapshotPath)));
    }

    public FastObservableCollection<ThreadItem> Threads { get; } = [];

    public FastObservableCollection<ThreadItem> ArchivedThreads { get; } = [];

    private readonly record struct BuiltThreads(List<ThreadItem> Inbox, List<ThreadItem> Archived);

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
    public bool AllSelectedAreUnread => SelectedThreads.Count > 0 && SelectedThreads.All(t => t.IsUnread);

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
        OnPropertyChanged(nameof(AllSelectedAreUnread));
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

        // The row moves to the archive straight away; the reload rebuilds both lists.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var row = Threads.FirstOrDefault(t => t.ThreadId == thread.ThreadId);
            if (row is not null)
            {
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

        // The moved rows are shown in the archive straight away; the reload below rebuilds both lists.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var t in selected)
            {
                Threads.Remove(t);
                t.IsSelected = false;
            }
            ExitSelectionMode();
            ArchivedThreads.AddRange(selected);
            _ = LoadThreadsAsync();
        });

        return Task.FromResult(true);
    }

    public async Task MarkSelectedAsUnreadAsync()
    {
        var selected = SelectedThreads;
        if (selected.Count == 0)
            return;

        var makeUnread = !AllSelectedAreUnread;

        foreach (var t in selected)
        {
            if (makeUnread)
            {
                t.MarkAsUnread();
                _ = Task.Run(() => _smsService.MarkThreadAsUnreadAsync(t.ThreadId));
            }
            else
            {
                t.MarkAsRead();
                _ = Task.Run(() => _smsService.MarkThreadAsReadAsync(t.ThreadId));
            }
        }

        ExitSelectionMode();
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
        if (Threads.Count > 0)
            ChatViewModel.PreloadVisibleThreads(Threads.Take(PreloadCount).ToList(), _smsService, _dateFormatter, PreloadCount);
    }

    public bool HasPermission
    {
        get => _hasPermission;
        private set => SetField(ref _hasPermission, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            // Back on the inbox (tab switch, chat closed, app resumed): the list is only refreshed
            // by incoming SMS otherwise, so sent replies, read state and "Yesterday" labels go stale.
            // Prompts are not repeated; they ran once and re-launching the role dialog on every
            // return to the tab is worse than an empty list.
            _ = LoadThreadsAsync();
            return;
        }

        _initialized = true;

        // Paint the last known inbox right away; the real query below refreshes it.
        if (Threads.Count == 0)
        {
            var cached = await (_snapshotTask ??= StartSnapshotLoad());
            if (cached.Inbox.Count > 0 && Threads.Count == 0)
            {
                Threads.Reset(cached.Inbox);
                if (ArchivedThreads.Count == 0)
                    ArchivedThreads.Reset(cached.Archived);
            }
        }

        await _smsService.EnsureDefaultSmsAppAsync();
        var granted = await _smsService.EnsurePermissionsAsync();
        HasPermission = granted;
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

            MainThread.BeginInvokeOnMainThread(() =>
            {
                ApplyItems(Threads, items.Inbox);
                ApplyItems(ArchivedThreads, items.Archived);
            });

            if (HasPermission)
                _ = Task.Run(() => ThreadSnapshotStore.Save(SnapshotPath, rawThreads));

            // Before the screens are warmed up the CPU belongs to them; EnableChatPreload starts this later.
            if (_chatPreloadEnabled)
            {
                var newest = items.Inbox.Take(PreloadCount).ToList();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(800);
                    ChatViewModel.PreloadVisibleThreads(newest, _smsService, _dateFormatter, PreloadCount);
                });
            }
        }
        finally
        {
            IsLoading = false;
            if (_reloadPending)
            {
                _reloadPending = false;
                _ = LoadThreadsAsync();
            }
        }
    }

    private BuiltThreads BuildItems(IReadOnlyList<SmsThread> rawThreads)
    {
        var archivedIds = GetArchivedThreadIds();
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentUICulture;

        var items = new List<ThreadItem>(rawThreads.Count);
        var archived = new List<ThreadItem>();
        foreach (var t in rawThreads)
        {
            var name = !string.IsNullOrWhiteSpace(t.ContactName)
                ? t.ContactName
                : PhoneNumberNormalizer.IsAlphanumeric(t.Address)
                    ? t.Address
                    : PhoneNumberNormalizer.FormatDisplay(t.Address);

            (archivedIds.Contains(t.ThreadId) ? archived : items).Add(new ThreadItem
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
                Preview = t.Snippet,
                IsUnread = t.UnreadCount > 0,
                HasFailed = t.HasFailed
            });
        }

        return new BuiltThreads(items, archived);
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
