using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public sealed class MessagesViewModel : INotifyPropertyChanged
{
    private readonly ISmsService _smsService;
    private readonly IDateFormattingService _dateFormatter;
    private bool _isLoading;
    private bool _hasPermission = true;
    private bool _initialized;

    private const string ArchivedKey = "archived_threads_v1";

    public MessagesViewModel(ISmsService? smsService = null, IDateFormattingService? dateFormatter = null)
    {
        _smsService = smsService ?? IPlatformApplication.Current?.Services.GetService<ISmsService>() ?? throw new InvalidOperationException("ISmsService not resolved");
        _dateFormatter = dateFormatter ?? IPlatformApplication.Current?.Services.GetService<IDateFormattingService>() ?? new DateFormattingService();

        SmsDeliverReceiver.SmsReceived += OnSmsReceived;
    }

    public FastObservableCollection<ThreadItem> Threads { get; } = [];

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

    public async Task<bool> DeleteSelectedThreadsAsync()
    {
        var selected = SelectedThreads;
        if (selected.Count == 0)
            return false;

        var threadIds = selected.Select(t => t.ThreadId).ToList();
        var success = await _smsService.DeleteThreadsAsync(threadIds);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var t in selected)
            {
                Threads.Remove(t);
            }
            ExitSelectionMode();
        });

        return success;
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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var t in selected)
            {
                Threads.Remove(t);
            }
            ExitSelectionMode();
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

    public bool HasPermission
    {
        get => _hasPermission;
        private set => SetField(ref _hasPermission, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized && Threads.Count > 0)
            return;

        _initialized = true;
        await _smsService.EnsureDefaultSmsAppAsync();
        var granted = await _smsService.EnsurePermissionsAsync();
        HasPermission = granted;
        await LoadThreadsAsync();
    }

    public async Task LoadThreadsAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            var rawThreads = await _smsService.GetThreadsAsync();
            var archivedIds = GetArchivedThreadIds();
            var now = DateTime.Now;
            var culture = CultureInfo.CurrentUICulture;

            var items = new List<ThreadItem>(rawThreads.Count);
            foreach (var t in rawThreads)
            {
                if (archivedIds.Contains(t.ThreadId))
                    continue;

                var name = !string.IsNullOrWhiteSpace(t.ContactName)
                    ? t.ContactName
                    : PhoneNumberNormalizer.IsAlphanumeric(t.Address)
                        ? t.Address
                        : PhoneNumberNormalizer.FormatDisplay(t.Address);

                items.Add(new ThreadItem
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

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Threads.Reset(items);
            });

            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                ChatViewModel.PreloadVisibleThreads(items.Take(12), _smsService, _dateFormatter);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnSmsReceived()
    {
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
