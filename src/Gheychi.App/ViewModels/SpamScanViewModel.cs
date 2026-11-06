using System.ComponentModel;
using System.Globalization;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.ViewModels;

public enum ScanStage
{
    /// <summary>Nothing running and nothing to review: the start screen.</summary>
    Idle,
    Scanning,
    /// <summary>A finished scan's hits wait for the user to approve them.</summary>
    Review,
    /// <summary>A scan that found nothing to review, or could not run.</summary>
    Finished,
    Applying
}

public enum ScanFilter
{
    All,
    Strong,
    Maybe
}

public enum ApplyResult
{
    Moved,
    NothingMoved,
    NotDefaultApp
}

/// <summary>
/// The history scan: reads the received messages, judges them, and lets the user approve which ones move to Spam.
/// Nothing leaves the inbox until <see cref="ApplyAsync"/>. A singleton, so a scan keeps going while its screen is closed.
/// </summary>
public sealed class SpamScanViewModel : INotifyPropertyChanged
{
    private const int ApplyChunk = 50;

    private readonly SpamHistoryScan _scanner;
    private readonly ISpamScanStore _store;
    private readonly ISpamMessageRepository _repository;
    private readonly ISpamStatsRepository _stats;
    private readonly ISmsService _sms;
    private readonly ISpamSettings _settings;
    private readonly ITrustedSenders _trusted;
    private readonly SpamViewModel _spam;
    private readonly IDateFormattingService _dates;
    private readonly List<ScanGroupRow> _groups = [];
    private CancellationTokenSource? _stop;
    private SpamScanRecord? _scan;
    private Task? _pendingLoad;
    private ScanFilter _filter;

    public SpamScanViewModel(
        SpamHistoryScan scanner,
        ISpamScanStore store,
        ISpamMessageRepository repository,
        ISpamStatsRepository stats,
        ISmsService sms,
        ISpamSettings settings,
        ITrustedSenders trusted,
        SpamViewModel spam,
        IDateFormattingService dates)
    {
        _scanner = scanner;
        _store = store;
        _repository = repository;
        _stats = stats;
        _sms = sms;
        _settings = settings;
        _trusted = trusted;
        _spam = spam;
        _dates = dates;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the main thread when the stage or the progress changes, for the Spam tab's banner.</summary>
    public event Action? Changed;

    /// <summary>Raised on the main thread after messages moved: the scan id and how many.</summary>
    public event Action<long, int>? Applied;

    public ScanStage Stage { get; private set; }

    public int Total { get; private set; }
    public int Checked { get; private set; }
    public int Found { get; private set; }
    public double Fraction => Total == 0 ? 0 : Math.Clamp((double)Checked / Total, 0, 1);
    public int Percent => (int)Math.Round(Fraction * 100);

    /// <summary>What the start screen says will be checked; null until <see cref="PrepareAsync"/> has counted.</summary>
    public int? ReceivedCount { get; private set; }
    public int KeptCount { get; private set; }
    public bool FilterOff => !_settings.Enabled;

    /// <summary>Why the last scan ended without a review, for the finished screen.</summary>
    public string FinishedText { get; private set; } = string.Empty;
    public string FinishedDetail { get; private set; } = string.Empty;

    public string ReviewHeader { get; private set; } = string.Empty;
    /// <summary>Stopped part-way or with messages the model could not score; empty otherwise.</summary>
    public string ReviewNote { get; private set; } = string.Empty;
    public bool HasMaybe { get; private set; }
    public ScanFilter Filter => _filter;

    public FastObservableCollection<object> Rows { get; } = new();

    public IEnumerable<ScanMessageRow> SelectedMessages => _groups.SelectMany(g => g.All).Where(m => m.Selected && !m.Kept);
    public int SelectedCount => SelectedMessages.Count();

    /// <summary>Picks up a review left from an earlier run of the app. Call from the UI thread.</summary>
    public Task LoadPendingAsync() => _pendingLoad ??= LoadPendingCoreAsync();

    /// <summary>Counts what a scan would check, for the start screen.</summary>
    public async Task PrepareAsync()
    {
        try
        {
            var (received, kept) = await Task.Run(async () => ((await _sms.GetReceivedMessagesAsync()).Count, (await _store.GetKeptAsync()).Count));
            ReceivedCount = received;
            KeptCount = kept;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Counting messages for the scan failed: {ex}");
        }

        RaiseAll();
    }

    public async Task StartAsync()
    {
        if (Stage is ScanStage.Scanning or ScanStage.Applying || FilterOff)
            return;

        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        var started = DateTime.Now;
        Total = Checked = Found = 0;
        SetStage(ScanStage.Scanning);

        try
        {
            var threshold = _settings.Threshold;
            var (messages, skip) = await Task.Run(async () =>
            {
                var received = await _sms.GetReceivedMessagesAsync(token);
                var skipped = new HashSet<string>(await _store.GetKeptAsync(), StringComparer.Ordinal);
                // A message the user moved back from Spam was already judged by them.
                foreach (var stat in await _stats.GetAllAsync())
                {
                    if (stat.Restored)
                        skipped.Add(SpamHistoryScan.Fingerprint(stat.SenderKey, new DateTimeOffset(stat.Timestamp).ToUnixTimeMilliseconds()));
                }
                return (received, skipped);
            }, token);

            Total = messages.Count;
            RaiseAll();

            var progress = new Progress<SpamScanProgress>(p =>
            {
                Checked = p.Checked;
                Found = p.Found;
                RaiseAll();
            });
            var result = await Task.Run(() => _scanner.RunAsync(messages, skip, threshold, progress, token));
            Checked = result.Checked;

            if (result.ModelFailed)
            {
                Finish(LocalizationManager.Instance["Scan_ModelFailed"], string.Empty);
                return;
            }

            if (result.Candidates.All(c => c.Section == SpamScanSection.Sender))
            {
                var loc = LocalizationManager.Instance;
                Finish(
                    result.Stopped
                        ? string.Format(loc["Scan_StoppedAt"], N(result.Checked), N(result.Total), N(0))
                        : string.Format(loc["Scan_None"], N(result.Checked - result.Skipped)),
                    Unchecked(result.Unchecked));
                return;
            }

            var record = new SpamScanRecord(0, started, null, result.Total, result.Checked, result.Unchecked, result.Stopped);
            var id = await _store.SavePendingAsync(record, result.Candidates);
            if (id <= 0)
            {
                Finish(LocalizationManager.Instance["Scan_SaveFailed"], string.Empty);
                return;
            }

            ShowReview(record with { Id = id }, result.Candidates);
        }
        catch (OperationCanceledException)
        {
            SetStage(ScanStage.Idle);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spam scan failed: {ex}");
            Finish(LocalizationManager.Instance["Scan_Failed"], string.Empty);
        }
        finally
        {
            _stop?.Dispose();
            _stop = null;
        }
    }

    public void Stop() => _stop?.Cancel();

    /// <summary>Back to the start screen from the finished one.</summary>
    public void Dismiss()
    {
        if (Stage == ScanStage.Finished)
            SetStage(ScanStage.Idle);
    }

    public void Toggle(ScanMessageRow row)
    {
        if (row.Candidate is null || row.Kept)
            return;

        row.Selected = !row.Selected;
        GroupOf(row)?.RaiseCheckChanged();
        SaveSelection([row], row.Selected);
    }

    public void ToggleGroup(ScanGroupRow group)
    {
        var rows = group.All.Where(m => !m.Kept && Visible(m)).ToList();
        var select = rows.Any(m => !m.Selected);
        foreach (var row in rows)
            row.Selected = select;
        group.RaiseCheckChanged();
        SaveSelection(rows, select);
    }

    public void ToggleExpanded(ScanGroupRow group)
    {
        group.Expanded = !group.Expanded;
        Rebuild();
    }

    public void SetFilter(ScanFilter filter)
    {
        if (_filter == filter)
            return;

        _filter = filter;
        Rebuild();
        RaiseAll();
    }

    /// <summary>The user says it is not spam: never moved, and later scans leave it out.</summary>
    public async Task MarkNotSpamAsync(ScanMessageRow row)
    {
        if (row.Candidate is not { } candidate)
            return;

        row.Selected = false;
        row.Kept = true;
        GroupOf(row)?.RaiseCheckChanged();
        SaveSelection([row], false);
        await _store.KeepAsync([(candidate.Sms.Address, candidate.Sms.DateMillis)]);
        RaiseAll();
    }

    /// <summary>Trusts the sender and takes all of its messages out of the move.</summary>
    public void TrustSender(ScanGroupRow group)
    {
        if (group.All.FirstOrDefault() is not { } first)
            return;

        _trusted.SetTrusted(first.Address, true);
        var rows = group.All.ToList();
        foreach (var row in rows)
            row.Selected = false;
        group.RaiseCheckChanged();
        SaveSelection(rows, false);
        RaiseAll();
    }

    public ScanGroupRow? GroupOf(ScanMessageRow row) => _groups.FirstOrDefault(g => g.All.Contains(row));

    public async Task DiscardAsync()
    {
        await _store.DiscardPendingAsync();
        ClearReview();
        SetStage(ScanStage.Idle);
        await PrepareAsync();
    }

    /// <summary>Conversations that would leave the inbox: every message in them is about to move.</summary>
    public async Task<int> CountEmptiedThreadsAsync()
    {
        try
        {
            var moving = SelectedMessages.GroupBy(m => m.Candidate!.Sms.ThreadId).ToDictionary(g => g.Key, g => g.Count());
            var threads = await _sms.GetThreadsAsync();
            // A draft keeps its conversation in the inbox.
            return threads.Count(t => !t.HasDraft && moving.TryGetValue(t.ThreadId, out var count) && count >= t.TotalCount);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Counting emptied conversations failed: {ex}");
            return 0;
        }
    }

    /// <summary>
    /// Moves the selected messages to Spam: each is copied into the quarantine first and only then deleted from the
    /// phone's store, and only while that row is still the one the user approved.
    /// </summary>
    public async Task<(ApplyResult Result, int Moved, int Changed)> ApplyAsync()
    {
        if (_scan is not { } scan || Stage != ScanStage.Review)
            return (ApplyResult.NothingMoved, 0, 0);

        if (!_sms.IsDefaultSmsApp() && !await _sms.EnsureDefaultSmsAppAsync())
            return (ApplyResult.NotDefaultApp, 0, 0);

        var selected = SelectedMessages.Select(m => m.Candidate!).ToList();
        if (selected.Count == 0)
            return (ApplyResult.NothingMoved, 0, 0);

        SetStage(ScanStage.Applying);
        var appliedAt = DateTime.Now;
        var threshold = _settings.Threshold;
        int moved = 0, changed = 0;
        try
        {
            // Applied before anything moves: if the app dies part-way, what already moved shows as this scan's batch
            // instead of being orphaned under a scan that is still "waiting for review".
            if (!await _store.MarkAppliedAsync(scan.Id, appliedAt))
            {
                SetStage(ScanStage.Review);
                return (ApplyResult.NothingMoved, 0, 0);
            }

            (moved, changed) = await Task.Run(async () =>
            {
                int movedCount = 0, changedCount = 0;
                foreach (var chunk in selected.Chunk(ApplyChunk))
                {
                    var stored = new List<(SpamScanCandidate Candidate, long Id)>();
                    foreach (var c in chunk)
                    {
                        var id = await _repository.AddAsync(new SpamMessage(0, c.Sms.Address, c.Sms.Body,
                            DateTimeOffset.FromUnixTimeMilliseconds(c.Sms.DateMillis).LocalDateTime, c.Sms.SubId, c.Score,
                            c.ModelVersion, threshold, scan.Id, c.Sms.Unread));
                        if (id > 0)
                            stored.Add((c, id));
                    }

                    var removed = (await _sms.RemoveReceivedAsync(stored.Select(s => s.Candidate.Sms).ToList())).ToHashSet();
                    // Changed since the scan (or the delete failed): it stays in the inbox, so its copy goes.
                    await _repository.DeleteAsync(stored.Where(s => !removed.Contains(s.Candidate.Sms.Id)).Select(s => s.Id).ToList());
                    movedCount += removed.Count;
                    changedCount += chunk.Length - removed.Count;
                }
                return (movedCount, changedCount);
            });

            // An applied scan with nothing in it is never shown, and retention still clears any copy a failed
            // delete above left under its id.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Moving scanned spam failed: {ex}");
        }

        ClearReview();
        SetStage(ScanStage.Idle);
        NotificationActionReceiver.RaiseThreadsChanged(0);
        await _spam.ReloadAsync();
        if (moved > 0)
            Applied?.Invoke(scan.Id, moved);
        return (moved > 0 ? ApplyResult.Moved : ApplyResult.NothingMoved, moved, changed);
    }

    /// <summary>A batch the scan moved to Spam, grouped by sender like the review; <paramref name="expanded"/> keeps groups open across rebuilds.</summary>
    public IReadOnlyList<ScanGroupRow> BatchGroups(SpamBatchItem batch, ISet<string> expanded)
    {
        var culture = CultureInfo.CurrentUICulture;
        return batch.Items
            .GroupBy(i => PhoneNumberNormalizer.ToLookupKey(i.Message.Address))
            .Select(g => new ScanGroupRow
            {
                Key = g.Key,
                Sender = g.First().Sender,
                Hits = g.Select(i => new ScanMessageRow
                {
                    Item = i,
                    Address = i.Message.Address,
                    Sender = i.Sender,
                    Body = i.Message.Body,
                    Score = i.Message.Score,
                    Time = i.Message.Timestamp,
                    DateText = _dates.FormatDateSeparator(i.Message.Timestamp, DateTime.Now, culture)
                }).ToList(),
                Others = [],
                CountText = Messages(g.Count()),
                Expanded = expanded.Contains(g.Key)
            })
            .OrderByDescending(g => g.Hits.Count)
            .ThenByDescending(g => g.Hits[0].Time)
            .ToList();
    }

    private async Task LoadPendingCoreAsync()
    {
        try
        {
            if (await _store.GetPendingAsync() is { } pending && Stage == ScanStage.Idle)
                ShowReview(pending.Scan, pending.Candidates);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loading the pending scan failed: {ex}");
        }
    }

    private void ShowReview(SpamScanRecord scan, IReadOnlyList<SpamScanCandidate> candidates)
    {
        _scan = scan;
        _filter = ScanFilter.All;
        _groups.Clear();

        var culture = CultureInfo.CurrentUICulture;
        ScanMessageRow Row(SpamScanCandidate c) => new()
        {
            Candidate = c,
            Address = c.Sms.Address,
            Sender = PhoneNumberNormalizer.FormatDisplay(c.Sms.Address),
            Body = c.Sms.Body,
            Score = c.Score,
            Time = DateTimeOffset.FromUnixTimeMilliseconds(c.Sms.DateMillis).LocalDateTime,
            DateText = _dates.FormatDateSeparator(DateTimeOffset.FromUnixTimeMilliseconds(c.Sms.DateMillis).LocalDateTime, DateTime.Now, culture),
            Selected = c.Selected
        };

        foreach (var g in candidates.GroupBy(c => PhoneNumberNormalizer.ToLookupKey(c.Sms.Address)))
        {
            var hits = g.Where(c => c.Section != SpamScanSection.Sender).OrderByDescending(c => c.Sms.DateMillis).Select(Row).ToList();
            if (hits.Count == 0)
                continue;

            var others = g.Where(c => c.Section == SpamScanSection.Sender).OrderByDescending(c => c.Sms.DateMillis).Select(Row).ToList();
            _groups.Add(new ScanGroupRow
            {
                Key = g.Key,
                Sender = hits[0].Sender,
                Hits = hits,
                Others = others,
                CountText = Messages(hits.Count),
                ShowCheck = true,
                Expanded = hits.Count + others.Count <= 3
            });
        }

        _groups.Sort((a, b) =>
        {
            var strong = IsStrong(b).CompareTo(IsStrong(a));
            if (strong != 0)
                return strong;
            var count = b.Hits.Count.CompareTo(a.Hits.Count);
            return count != 0 ? count : b.Hits[0].Time.CompareTo(a.Hits[0].Time);
        });

        var loc = LocalizationManager.Instance;
        var hitCount = _groups.Sum(g => g.Hits.Count);
        ReviewHeader = string.Format(loc["Scan_ReviewHeader"], N(hitCount), N(_groups.Count));
        var notes = new List<string>();
        if (scan.Stopped)
            notes.Add(string.Format(loc["Scan_StoppedNote"], N(scan.Checked), N(scan.Total)));
        if (scan.Unchecked > 0)
            notes.Add(Unchecked(scan.Unchecked));
        ReviewNote = string.Join(" ", notes);
        HasMaybe = _groups.Any(g => g.All.Any(m => m.Candidate!.Section != SpamScanSection.Strong));

        Rebuild();
        SetStage(ScanStage.Review);
    }

    private static bool IsStrong(ScanGroupRow group) => group.Hits.Any(m => m.Candidate!.Section == SpamScanSection.Strong);

    private bool Visible(ScanMessageRow row) => _filter switch
    {
        ScanFilter.Strong => row.Candidate?.Section == SpamScanSection.Strong,
        ScanFilter.Maybe => row.Candidate?.Section != SpamScanSection.Strong,
        _ => true
    };

    private void Rebuild()
    {
        var loc = LocalizationManager.Instance;
        var rows = new List<object>();
        var maybeTitled = false;
        foreach (var group in _groups)
        {
            var hits = group.Hits.Where(Visible).ToList();
            var others = group.Others.Where(Visible).ToList();
            if (hits.Count == 0 && others.Count == 0)
                continue;

            if (!IsStrong(group) && !maybeTitled && _filter == ScanFilter.All)
            {
                rows.Add(new ScanNoteRow(loc["Scan_SectionMaybe"], Indented: false));
                maybeTitled = true;
            }

            rows.Add(group);
            if (!group.Expanded)
                continue;

            rows.AddRange(hits);
            if (others.Count > 0)
            {
                rows.Add(new ScanNoteRow(string.Format(loc["Scan_MoreFromSender"], N(others.Count)), Indented: true));
                rows.AddRange(others);
            }
        }

        Rows.Reset(rows);
    }

    private void ClearReview()
    {
        _scan = null;
        _groups.Clear();
        Rows.Reset([]);
    }

    private void SaveSelection(IReadOnlyCollection<ScanMessageRow> rows, bool selected)
    {
        RaiseAll();
        if (_scan is not { } scan)
            return;

        var ids = rows.Where(r => r.Candidate is not null).Select(r => r.Candidate!.Sms.Id).ToList();
        _ = _store.SetSelectedAsync(scan.Id, ids, selected);
    }

    private void Finish(string text, string detail)
    {
        FinishedText = text;
        FinishedDetail = detail;
        SetStage(ScanStage.Finished);
    }

    private void SetStage(ScanStage stage)
    {
        Stage = stage;
        RaiseAll();
    }

    private static string N(int value) => LocalizedNumbers.Number(value);

    private static string Unchecked(int count) =>
        count == 0 ? string.Empty : string.Format(LocalizationManager.Instance["Scan_Unchecked"], N(count));

    private static string Messages(int count) => string.Format(LocalizationManager.Instance["Scan_Messages"], N(count));

    // The screen binds to a handful of computed values; raising them together is simpler than tracking each.
    private void RaiseAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scan Changed handler failed: {ex}");
        }
    }
}
