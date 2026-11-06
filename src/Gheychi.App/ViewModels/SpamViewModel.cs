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
    private readonly ISpamStatsRepository _stats;
    private readonly ISpamScanStore _scans;
    private readonly ISmsService _smsService;
    private readonly ISpamSettings _settings;
    private readonly ITrustedSenders _trustedSenders;
    private readonly IDateFormattingService _dateFormatter;
    // Caught on arrival, newest first. A history scan's messages live in their batch instead.
    private readonly List<SpamItem> _all = [];
    private readonly List<SpamBatchItem> _batches = [];
    // Messages being put back right now: restore all, undo and a single restore can overlap.
    private readonly HashSet<long> _restoring = [];
    private Task? _loadTask;
    private bool _reloadPending;
    private string _searchText = string.Empty;

    public SpamViewModel(
        ISpamMessageRepository repository,
        ISpamStatsRepository stats,
        ISpamScanStore scans,
        ISmsService smsService,
        ISpamSettings settings,
        ITrustedSenders trustedSenders,
        IDateFormattingService dateFormatter)
    {
        _repository = repository;
        _stats = stats;
        _scans = scans;
        _smsService = smsService;
        _settings = settings;
        _trustedSenders = trustedSenders;
        _dateFormatter = dateFormatter;
        SmsDeliverReceiver.SpamReceived += OnSpamReceived;
    }

    /// <summary>Raised on the main thread whenever a message was added or removed.</summary>
    public event Action? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The shown rows, newest first: <see cref="SpamItem"/>s and <see cref="SpamBatchItem"/>s under a
    /// <see cref="DateSeparatorItem"/> per day. A search lists a batch's matching messages on their own.
    /// </summary>
    public FastObservableCollection<object> Items { get; } = new();

    public bool HasAny => Count > 0;

    public string Subtitle => string.Format(LocalizationManager.Instance["Spam_Subtitle"], _settings.RetentionDays);

    public int Count => _all.Count + _batches.Sum(b => b.Items.Count);

    public int RetentionDays => _settings.RetentionDays;

    /// <summary>How many messages batch restores are still putting back; 0 when none is running.</summary>
    public int RestoringLeft { get; private set; }

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

    /// <summary>Reads everything again, e.g. after a history scan moved messages here. Call from the UI thread.</summary>
    public Task ReloadAsync()
    {
        if (_loadTask is { IsCompleted: false })
        {
            _reloadPending = true;
            return _loadTask;
        }

        return _loadTask = LoadAsync();
    }

    public IReadOnlyList<SpamItem> ForSender(string address)
    {
        var key = PhoneNumberNormalizer.ToLookupKey(address);
        return key.Length == 0
            ? []
            : AllItems().Where(i => PhoneNumberNormalizer.ToLookupKey(i.Message.Address) == key).OrderByDescending(i => i.Message.Timestamp).ToList();
    }

    public SpamBatchItem? Batch(long scanId) => _batches.FirstOrDefault(b => b.ScanId == scanId);

    public bool IsTrusted(string address) => _trustedSenders.IsTrusted(address);

    public void SetTrusted(string address, bool trusted) => _trustedSenders.SetTrusted(address, trusted);

    public IReadOnlyList<string> TrustedSenders() => _trustedSenders.GetAll();

    public int CountSince(DateTime since) => _all.Count(i => i.Message.Timestamp >= since);

    public async Task<SpamAnalytics> GetAnalyticsAsync()
    {
        var stats = await _stats.GetAllAsync();
        return await Task.Run(() => SpamAnalytics.Compute(stats, DateTime.Now));
    }

    /// <summary>Saves the new retention and drops what is now past it.</summary>
    public async Task SetRetentionDaysAsync(int days)
    {
        _settings.RetentionDays = days;
        OnPropertyChanged(nameof(Subtitle));

        var cutoff = DateTime.Now.AddDays(-days);
        await _repository.DeleteOlderThanAsync(cutoff);
        var expiredScans = _batches.Where(b => b.AppliedAt < cutoff).Select(b => b.ScanId).ToList();
        await DeleteScansAsync(expiredScans);

        var expired = _all.Where(i => i.Message.Timestamp < cutoff).ToList();
        _batches.RemoveAll(b => expiredScans.Contains(b.ScanId));
        _all.RemoveAll(expired.Contains);
        ApplyFilter();
        RaiseChanged();
    }

    /// <summary>When a scan's messages are deleted: the day the scan moved them, plus the retention.</summary>
    public DateTime ExpiresOn(DateTime appliedAt) => appliedAt.Date.AddDays(_settings.RetentionDays);

    /// <summary>Moves the message back to the inbox; with <paramref name="trustSender"/> its sender also skips the spam check from now on.</summary>
    public async Task<bool> RestoreAsync(SpamItem item, bool trustSender)
    {
        if (!await RestoreOneAsync(item, keep: true))
            return false;

        if (trustSender)
            _trustedSenders.SetTrusted(item.Message.Address, true);

        Remove([item]);
        return true;
    }

    /// <summary>
    /// Moves a whole scan's messages back. <paramref name="keep"/> also tells later scans to leave them out; an undo
    /// does not, since the user took back the move rather than judged the messages. Returns how many came back.
    /// </summary>
    public async Task<int> RestoreBatchAsync(long scanId, bool keep)
    {
        if (Batch(scanId) is not { } batch)
            return 0;

        // Off the list at once: while it goes back it cannot be opened, restored or undone again.
        _batches.Remove(batch);
        var messages = batch.Items.Select(i => i.Message).Where(m => _restoring.Add(m.Id)).ToList();
        var left = messages.Count;
        RestoringLeft += left;
        ApplyFilter();
        RaiseChanged();

        var restored = new HashSet<long>();
        try
        {
            // In chunks, each deleted from quarantine right after it is in the inbox, so a crash leaves at most one
            // chunk in both places and never loses a message.
            foreach (var chunk in messages.Chunk(50))
            {
                var stored = (await _smsService.RestoreIncomingManyAsync(chunk)).ToHashSet();
                var back = chunk.Where(m => stored.Contains(m.Id)).ToList();
                restored.UnionWith(stored);
                if (keep && back.Count > 0)
                    await _scans.KeepAsync(back.Select(m => (m.Address, new DateTimeOffset(m.Timestamp).ToUnixTimeMilliseconds())).ToList());
                await _repository.DeleteAsync(stored);

                left -= chunk.Length;
                RestoringLeft -= chunk.Length;
                RaiseChanged();
            }
        }
        finally
        {
            RestoringLeft -= left;
            NotificationActionReceiver.RaiseThreadsChanged(0);
            if (restored.Count < messages.Count)
            {
                // What did not go back is still in quarantine: show it again.
                _restoring.ExceptWith(messages.Select(m => m.Id).Where(id => !restored.Contains(id)));
                await ReloadAsync();
            }
            else
            {
                RaiseChanged();
            }
        }

        return restored.Count;
    }

    public async Task DeleteAsync(SpamItem item)
    {
        if (_restoring.Contains(item.Id))
            return;

        await _repository.DeleteAsync([item.Id]);
        Remove([item]);
    }

    public async Task ClearAllAsync()
    {
        await _repository.DeleteAllAsync();
        await _scans.DeleteAsync(_batches.Select(b => b.ScanId).ToList());
        _all.Clear();
        _batches.Clear();
        ApplyFilter();
        RaiseChanged();
    }

    private async Task<bool> RestoreOneAsync(SpamItem item, bool keep)
    {
        var message = item.Message;
        // Stays marked once back in the inbox (quarantine ids are never reused), so a stale row cannot restore it twice.
        if (!_restoring.Add(message.Id))
            return false;

        var threadId = await _smsService.RestoreIncomingAsync(message.Address, message.Body, message.Timestamp, message.SubId, read: !message.Unread);
        if (threadId <= 0)
        {
            _restoring.Remove(message.Id);
            return false;
        }

        if (message.ScanId == 0)
            await _stats.MarkRestoredAsync(message.Id);
        else if (keep)
            await _scans.KeepAsync([(message.Address, new DateTimeOffset(message.Timestamp).ToUnixTimeMilliseconds())]);

        await _repository.DeleteAsync([message.Id]);
        return true;
    }

    private async Task DeleteScansAsync(IReadOnlyCollection<long> scanIds)
    {
        if (scanIds.Count == 0)
            return;

        if (await _repository.DeleteScansAsync(scanIds))
            await _scans.DeleteAsync(scanIds);
    }

    private async Task LoadAsync()
    {
        do
        {
            _reloadPending = false;
            var cutoff = DateTime.Now.AddDays(-_settings.RetentionDays);
            var (items, batches) = await Task.Run(async () =>
            {
                await _repository.DeleteOlderThanAsync(cutoff);
                var scans = await _scans.GetAppliedAsync();
                await DeleteScansAsync(scans.Where(s => s.AppliedAt < cutoff).Select(s => s.Id).ToList());

                var messages = await _repository.GetAllAsync();
                var live = messages.Where(m => m.ScanId == 0).ToList();
                // Spam caught before the log existed, or whose log write failed in the receiver. A scan's messages stay
                // out: the analytics are about what the filter catches as it arrives.
                await _stats.AddMissingAsync(live.Select(SpamStats.From).ToList());

                var now = DateTime.Now;
                var culture = CultureInfo.CurrentUICulture;
                var byScan = messages.Where(m => m.ScanId != 0).ToLookup(m => m.ScanId);
                var built = scans
                    .Where(s => s.AppliedAt >= cutoff && byScan[s.Id].Any())
                    .Select(s => BuildBatch(s, byScan[s.Id].Select(m => ToItem(m, now, culture)).ToList()))
                    .ToList();
                return (live.Select(m => ToItem(m, now, culture)).ToList(), built);
            });

            // A restore running now has taken its messages off the list; a load must not bring them back.
            _all.Clear();
            _all.AddRange(items.Where(i => !_restoring.Contains(i.Id)));
            _batches.Clear();
            foreach (var batch in batches)
            {
                var shown = batch.Items.Where(i => !_restoring.Contains(i.Id)).ToList();
                if (shown.Count == batch.Items.Count)
                    _batches.Add(batch);
                else if (shown.Count > 0)
                    _batches.Add(BuildBatch(batch.Scan, shown));
            }

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
        if (_batches.Count > 0)
        {
            // A batch row can share today's header; laying the list out again keeps one header per day.
            ApplyFilter();
        }
        else if (Matches(item, SearchTextHelper.BuildVariants(_searchText)))
        {
            if (Items.Count > 1 && Items[1] is SpamItem newest && newest.Message.Timestamp.Date == item.Message.Timestamp.Date)
            {
                Items.Insert(1, item);
            }
            else
            {
                Items.Insert(0, item);
                Items.Insert(0, Header(item.Message.Timestamp));
            }
        }
        RaiseChanged();
    }

    private void Remove(IReadOnlyCollection<SpamItem> items)
    {
        if (items.Count == 0)
            return;

        var fromBatch = false;
        foreach (var item in items)
        {
            if (item.Message.ScanId == 0)
            {
                _all.Remove(item);
                continue;
            }

            fromBatch = true;
            var index = _batches.FindIndex(b => b.ScanId == item.Message.ScanId);
            if (index < 0)
                continue;

            var left = _batches[index].Items.Where(i => i != item).ToList();
            if (left.Count == 0)
                _batches.RemoveAt(index);
            else
                _batches[index] = BuildBatch(_batches[index].Scan, left);
        }

        if (fromBatch)
        {
            ApplyFilter();
            RaiseChanged();
            return;
        }

        foreach (var item in items)
        {
            var index = Items.IndexOf(item);
            if (index < 0)
                continue;

            Items.RemoveAt(index);
            // A day whose last row just went keeps no empty header.
            if (Items[index - 1] is DateSeparatorItem && (index == Items.Count || Items[index] is DateSeparatorItem))
                Items.RemoveAt(index - 1);
        }

        RaiseChanged();
    }

    private IEnumerable<SpamItem> AllItems() => _all.Concat(_batches.SelectMany(b => b.Items));

    private void ApplyFilter()
    {
        var needles = SearchTextHelper.BuildVariants(_searchText);
        if (needles.Count > 0)
        {
            Items.Reset(WithDayHeaders(AllItems()
                .Where(i => Matches(i, needles))
                .OrderByDescending(i => i.Message.Timestamp)
                .Select(i => ((object)i, i.Message.Timestamp))));
            return;
        }

        var rows = _all.Select(i => ((object)i, i.Message.Timestamp))
            .Concat(_batches.Select(b => ((object)b, b.AppliedAt)))
            .OrderByDescending(r => r.Item2);
        Items.Reset(WithDayHeaders(rows));
    }

    private IEnumerable<object> WithDayHeaders(IEnumerable<(object Row, DateTime Time)> rows)
    {
        DateTime? day = null;
        foreach (var (row, time) in rows)
        {
            if (time.Date != day)
            {
                day = time.Date;
                yield return Header(time);
            }

            yield return row;
        }
    }

    private DateSeparatorItem Header(DateTime timestamp) =>
        new(_dateFormatter.FormatDayHeader(timestamp, DateTime.Now, CultureInfo.CurrentUICulture));

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

    private SpamBatchItem BuildBatch(SpamScanRecord scan, IReadOnlyList<SpamItem> items)
    {
        var loc = LocalizationManager.Instance;
        var appliedAt = scan.AppliedAt ?? scan.StartedAt;
        var culture = CultureInfo.CurrentUICulture;
        var senders = items.Select(i => PhoneNumberNormalizer.ToLookupKey(i.Message.Address)).Distinct().Count();
        return new SpamBatchItem
        {
            Scan = scan,
            Items = items.OrderByDescending(i => i.Message.Timestamp).ToList(),
            Title = string.Format(loc["Scan_Batch"], LocalizedNumbers.Number(items.Count), LocalizedNumbers.Number(senders)),
            Subtitle = string.Format(loc["Scan_BatchExpires"], _dateFormatter.FormatDateSeparator(ExpiresOn(appliedAt), DateTime.Now, culture)),
            Clock = _dateFormatter.FormatMessageTime(appliedAt, culture)
        };
    }

    private SpamItem ToItem(SpamMessage message, DateTime now, CultureInfo culture) =>
        new()
        {
            Message = message,
            Sender = PhoneNumberNormalizer.FormatDisplay(message.Address),
            Confidence = LocalizationManager.Instance[
                SpamConfidence.Level(message.Score) == SpamConfidenceLevel.VeryLikely ? "Spam_VeryLikely" : "Spam_Likely"],
            Time = _dateFormatter.FormatThreadTime(message.Timestamp, now, culture),
            Clock = _dateFormatter.FormatMessageTime(message.Timestamp, culture)
        };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>The messages one history scan moved here, shown as a single row on the day of the scan.</summary>
public sealed class SpamBatchItem
{
    public required SpamScanRecord Scan { get; init; }
    public long ScanId => Scan.Id;
    public DateTime AppliedAt => Scan.AppliedAt ?? Scan.StartedAt;

    /// <summary>Newest first.</summary>
    public required IReadOnlyList<SpamItem> Items { get; init; }

    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Clock { get; init; }
}
