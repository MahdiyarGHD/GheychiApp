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

    public MessagesViewModel(ISmsService? smsService = null, IDateFormattingService? dateFormatter = null)
    {
        _smsService = smsService ?? IPlatformApplication.Current?.Services.GetService<ISmsService>() ?? throw new InvalidOperationException("ISmsService not resolved");
        _dateFormatter = dateFormatter ?? IPlatformApplication.Current?.Services.GetService<IDateFormattingService>() ?? new DateFormattingService();

        SmsDeliverReceiver.SmsReceived += OnSmsReceived;
    }

    public FastObservableCollection<ThreadItem> Threads { get; } = [];

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
            var now = DateTime.Now;
            var culture = CultureInfo.CurrentUICulture;

            var items = new List<ThreadItem>(rawThreads.Count);
            foreach (var t in rawThreads)
            {
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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
