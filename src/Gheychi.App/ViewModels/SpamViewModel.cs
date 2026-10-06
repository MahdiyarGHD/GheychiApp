using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.ViewModels;

/// <summary>Quarantined spam, shared by the Spam tab and the per-sender list on a profile.</summary>
public sealed class SpamViewModel : INotifyPropertyChanged
{
    private readonly ISpamMessageRepository _repository;
    private readonly ISmsService _smsService;
    private readonly ISpamSettings _settings;
    private readonly ITrustedSenders _trustedSenders;
    private readonly IDateFormattingService _dateFormatter;
    private readonly List<SpamItem> _all = [];
    private Task? _loadTask;
    private bool _reloadPending;
    private string _searchText = string.Empty;

    public SpamViewModel(
        ISpamMessageRepository repository,
        ISmsService smsService,
        ISpamSettings settings,
        ITrustedSenders trustedSenders,
        IDateFormattingService dateFormatter)
    {
        _repository = repository;
        _smsService = smsService;
        _settings = settings;
        _trustedSenders = trustedSenders;
        _dateFormatter = dateFormatter;
        SmsDeliverReceiver.SpamReceived += OnSpamReceived;
    }

    /// <summary>Raised on the main thread whenever a message was added or removed.</summary>
    public event Action? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public FastObservableCollection<SpamItem> Items { get; } = new();

    public bool HasAny => _all.Count > 0;

    public string Subtitle => string.Format(LocalizationManager.Instance["Spam_Subtitle"], _settings.RetentionDays);

    public int Count => _all.Count;

    public int RetentionDays => _settings.RetentionDays;

    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (_searchText == value)
                return;

            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    /// <summary>Loads once; later calls wait for that load. Call from the UI thread.</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    public IReadOnlyList<SpamItem> ForSender(string address)
    {
        var key = PhoneNumberNormalizer.ToLookupKey(address);
        return key.Length == 0
            ? []
            : _all.Where(i => PhoneNumberNormalizer.ToLookupKey(i.Message.Address) == key).ToList();
    }

    public bool IsTrusted(string address) => _trustedSenders.IsTrusted(address);

    public void SetTrusted(string address, bool trusted) => _trustedSenders.SetTrusted(address, trusted);

    public IReadOnlyList<string> TrustedSenders() => _trustedSenders.GetAll();

    public int CountSince(DateTime since) => _all.Count(i => i.Message.Timestamp >= since);

    /// <summary>Saves the new retention and drops what is now past it.</summary>
    public async Task SetRetentionDaysAsync(int days)
    {
        _settings.RetentionDays = days;
        OnPropertyChanged(nameof(Subtitle));

        var cutoff = DateTime.Now.AddDays(-days);
        await _repository.DeleteOlderThanAsync(cutoff);
        var expired = _all.Where(i => i.Message.Timestamp < cutoff).ToList();
        if (expired.Count > 0)
            Remove(expired);
    }

    /// <summary>Moves the message back to the inbox; with <paramref name="trustSender"/> its sender also skips the spam check from now on.</summary>
    public async Task<bool> RestoreAsync(SpamItem item, bool trustSender)
    {
        var message = item.Message;
        var threadId = await _smsService.RestoreIncomingAsync(message.Address, message.Body, message.Timestamp, message.SubId);
        if (threadId <= 0)
            return false;

        if (trustSender)
            _trustedSenders.SetTrusted(message.Address, true);

        await _repository.DeleteAsync([message.Id]);
        Remove([item]);
        return true;
    }

    public async Task DeleteAsync(SpamItem item)
    {
        await _repository.DeleteAsync([item.Id]);
        Remove([item]);
    }

    public async Task ClearAllAsync()
    {
        await _repository.DeleteAllAsync();
        Remove(_all.ToList());
    }

    private async Task LoadAsync()
    {
        do
        {
            _reloadPending = false;
            var cutoff = DateTime.Now.AddDays(-_settings.RetentionDays);
            var items = await Task.Run(async () =>
            {
                await _repository.DeleteOlderThanAsync(cutoff);
                var messages = await _repository.GetAllAsync();
                var now = DateTime.Now;
                var culture = CultureInfo.CurrentUICulture;
                return messages.Select(m => ToItem(m, now, culture)).ToList();
            });

            _all.Clear();
            _all.AddRange(items);
            ApplyFilter();
            RaiseChanged();
        }
        while (_reloadPending);
    }

    private void OnSpamReceived(SpamMessage message)
    {
        if (_loadTask is null)
            return;

        if (!_loadTask.IsCompleted)
        {
            _reloadPending = true;
            return;
        }

        if (_all.Any(i => i.Id == message.Id))
            return;

        var item = ToItem(message, DateTime.Now, CultureInfo.CurrentUICulture);
        _all.Insert(0, item);
        if (Matches(item, SearchTextHelper.BuildVariants(_searchText)))
            Items.Insert(0, item);
        RaiseChanged();
    }

    private void Remove(IReadOnlyCollection<SpamItem> items)
    {
        foreach (var item in items)
        {
            _all.Remove(item);
            Items.Remove(item);
        }

        RaiseChanged();
    }

    private void ApplyFilter()
    {
        var needles = SearchTextHelper.BuildVariants(_searchText);
        Items.Reset(needles.Count == 0 ? _all : _all.Where(i => Matches(i, needles)));
    }

    private static bool Matches(SpamItem item, IReadOnlyList<string> needles) =>
        needles.Count == 0
        || SearchTextHelper.ContainsAny(item.Sender, needles)
        || SearchTextHelper.ContainsAny(item.Message.Address, needles)
        || SearchTextHelper.ContainsAny(item.Message.Body, needles);

    private void RaiseChanged()
    {
        OnPropertyChanged(nameof(HasAny));
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spam Changed handler failed: {ex}");
        }
    }

    private SpamItem ToItem(SpamMessage message, DateTime now, CultureInfo culture) =>
        new()
        {
            Message = message,
            Sender = PhoneNumberNormalizer.FormatDisplay(message.Address),
            Confidence = LocalizationManager.Instance[
                SpamConfidence.Level(message.Score) == SpamConfidenceLevel.VeryLikely ? "Spam_VeryLikely" : "Spam_Likely"],
            Time = _dateFormatter.FormatThreadTime(message.Timestamp, now, culture)
        };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
