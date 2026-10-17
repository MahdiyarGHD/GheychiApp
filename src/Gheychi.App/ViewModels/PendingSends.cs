using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using Gheychi.App.Localization;
using Gheychi.Core.Scheduling;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

/// <summary>
/// The messages of one conversation that are waiting to go out: the ones held by the send delay, which can still be
/// undone, and the ones scheduled for later. The chat shows a bar for each kind.
/// </summary>
public sealed class PendingSends : INotifyPropertyChanged
{
    private const int PreviewLength = 60;

    private readonly IScheduledSendStore? _store;
    private readonly IScheduledSendScheduler? _scheduler;
    private readonly IDateFormattingService _dates;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _threadId;
    private bool _attached;
    private bool _hasDelayed;
    private bool _hasScheduled;
    private string _delayText = string.Empty;
    private string _delayPreview = string.Empty;
    private string _scheduledText = string.Empty;

    public PendingSends(IDateFormattingService dates)
    {
        _dates = dates;
        var services = IPlatformApplication.Current?.Services;
        _store = services?.GetService<IScheduledSendStore>();
        _scheduler = services?.GetService<IScheduledSendScheduler>();
        _tick.Tick += (_, _) => Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sends a message this class has claimed: the chat appends it and hands it to the radio.</summary>
    public Action<ScheduledMessage>? Send { get; set; }

    public bool HasDelayed => _hasDelayed;

    public string DelayText => _delayText;

    public string DelayPreview => _delayPreview;

    public bool HasScheduled => _hasScheduled;

    public string ScheduledText => _scheduledText;

    public bool CanFireExactly => _scheduler?.CanFireExactly ?? true;

    /// <summary>The conversation the bars show; called again when the chat switches to another.</summary>
    public void Bind(long threadId)
    {
        _threadId = threadId;
        Refresh();
    }

    /// <summary>Starts following the store while the chat is on screen.</summary>
    public void Attach()
    {
        if (_attached || _store is null)
            return;

        _attached = true;
        _store.Changed += OnStoreChanged;
        Refresh();
    }

    public void Detach()
    {
        if (!_attached || _store is null)
            return;

        _attached = false;
        _store.Changed -= OnStoreChanged;
        _tick.Stop();
    }

    /// <summary>Holds a typed message for the send delay. Returns false when there is nowhere to keep it, and the caller sends it at once.</summary>
    public bool Hold(long threadId, string address, string text, int subId, int delaySeconds)
    {
        if (_store is null || _scheduler is null || delaySeconds <= 0)
            return false;

        var message = new ScheduledMessage(NewId(), threadId, address, text, subId,
            DateTimeOffset.UtcNow.AddSeconds(delaySeconds).ToUnixTimeMilliseconds(), IsDelay: true);
        _store.Add(message);
        _scheduler.Schedule(message);
        _ = SendAfterAsync(message.Id, delaySeconds);
        return true;
    }

    /// <summary>The texts of the held messages, taken back so the user can edit them; they are not sent.</summary>
    public string Undo()
    {
        if (_store is null)
            return string.Empty;

        var texts = new List<string>();
        foreach (var held in _store.ForThread(_threadId).Where(m => m.IsDelay))
        {
            if (_store.Claim(held.Id) is { } taken)
            {
                _scheduler?.Cancel(taken.Id);
                texts.Add(taken.Text);
            }
        }

        return string.Join("\n", texts);
    }

    public bool Schedule(long threadId, string address, string text, int subId, long atMillis)
    {
        if (_store is null || _scheduler is null)
            return false;

        var message = new ScheduledMessage(NewId(), threadId, address, text, subId, atMillis, IsDelay: false);
        _store.Add(message);
        _scheduler.Schedule(message);
        return true;
    }

    public IReadOnlyList<ScheduledMessage> Scheduled() =>
        _store?.ForThread(_threadId).Where(m => !m.IsDelay).ToList() ?? [];

    public void SendNow(string id) => Dispatch(id);

    public void Delete(string id)
    {
        if (_store?.Claim(id) is { } taken)
            _scheduler?.Cancel(taken.Id);
    }

    /// <summary>Moves a scheduled message to another time.</summary>
    public void Reschedule(string id, long atMillis)
    {
        if (_store?.Claim(id) is not { } old || _scheduler is null)
            return;

        _scheduler.Cancel(old.Id);
        var moved = old with { Id = NewId(), SendAtMillis = atMillis };
        _store.Add(moved);
        _scheduler.Schedule(moved);
    }

    /// <summary>"Mon · Oct 5, 18:00": the day as the inbox words it, then the time.</summary>
    public string Describe(long atMillis)
    {
        var culture = CultureInfo.CurrentUICulture;
        var at = DateTimeOffset.FromUnixTimeMilliseconds(atMillis).LocalDateTime;
        return $"{_dates.FormatDayHeader(at, DateTime.Now, culture)}, {_dates.FormatMessageTime(at, culture)}";
    }

    private async Task SendAfterAsync(string id, int delaySeconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        MainThread.BeginInvokeOnMainThread(() => Dispatch(id));
    }

    // Whoever claims the message sends it: this timer, or the alarm behind it when the app was not running.
    private void Dispatch(string id)
    {
        if (_store?.Claim(id) is not { } message)
            return;

        _scheduler?.Cancel(message.Id);
        Send?.Invoke(message);
    }

    private void OnStoreChanged() => MainThread.BeginInvokeOnMainThread(Refresh);

    private void Refresh()
    {
        // Bind can come from a background thread, and the properties are bound to the UI.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        var pending = _store?.ForThread(_threadId) ?? [];
        var delayed = pending.Where(m => m.IsDelay).ToList();
        var scheduled = pending.Where(m => !m.IsDelay).ToList();
        var loc = LocalizationManager.Instance;

        var hasDelayed = delayed.Count > 0;
        var delayText = string.Empty;
        var delayPreview = string.Empty;
        if (hasDelayed)
        {
            var left = Math.Max(1, (int)Math.Ceiling((delayed.Max(m => m.SendAtMillis) - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0));
            delayText = string.Format(loc["Chat_SendingIn"], left);
            delayPreview = Preview(delayed[^1].Text);
        }

        var scheduledText = scheduled.Count == 0
            ? string.Empty
            : string.Format(loc["Chat_ScheduledBar"], scheduled.Count, Describe(scheduled[0].SendAtMillis));

        if (hasDelayed)
            _tick.Start();
        else
            _tick.Stop();

        Set(ref _hasDelayed, hasDelayed, nameof(HasDelayed));
        Set(ref _delayText, delayText, nameof(DelayText));
        Set(ref _delayPreview, delayPreview, nameof(DelayPreview));
        Set(ref _hasScheduled, scheduled.Count > 0, nameof(HasScheduled));
        Set(ref _scheduledText, scheduledText, nameof(ScheduledText));
    }

    public static string Preview(string text)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        return flat.Length <= PreviewLength ? flat : flat[..PreviewLength] + "…";
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
