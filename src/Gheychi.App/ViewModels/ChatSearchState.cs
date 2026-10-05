using System.ComponentModel;
using System.Runtime.CompilerServices;
using Gheychi.App.Localization;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.ViewModels;

public enum SearchMark
{
    None,
    Match,
    Current
}

/// <summary>
/// Search inside one open conversation. The conversation's message texts are read once when the search opens;
/// after that each keystroke is an in-memory scan on a worker thread, and only the newest keystroke's result is applied.
/// </summary>
public sealed class ChatSearchState(ISmsService smsService) : INotifyPropertyChanged
{
    private IReadOnlyList<ThreadSearchHit> _hits = [];
    private HashSet<long> _hitIds = [];
    private ThreadSearchIndex? _index;
    private Task<ThreadSearchIndex?>? _loading;
    private int _version;
    private int _current = -1;
    private int _anchorRow;
    private string _query = string.Empty;

    /// <summary>Raised after the hits or the current hit changed; the chat refreshes its outlines from it.</summary>
    public event Action? Changed;

    /// <summary>Raised when the user moved to another hit, or the first hit was picked: the chat should scroll to it.</summary>
    public event Action<ThreadSearchHit>? CurrentHitChanged;

    public bool IsActive { get; private set; }

    public string Query => _query;

    public int Count => _hits.Count;

    public bool HasHits => _hits.Count > 0;

    public bool CanNavigate => _hits.Count > 1;

    public ThreadSearchHit? CurrentHit => _current >= 0 && _current < _hits.Count ? _hits[_current] : null;

    public string CounterText
    {
        get
        {
            if (_query.Length == 0)
                return string.Empty;

            var loc = LocalizationManager.Instance;
            return _hits.Count == 0
                ? loc["Chat_SearchNoResults"]
                : string.Format(loc["Chat_SearchCounter"], _current + 1, _hits.Count);
        }
    }

    public SearchMark MarkFor(long messageId)
    {
        if (!IsActive || !_hitIds.Contains(messageId))
            return SearchMark.None;

        return CurrentHit is { } hit && hit.MessageId == messageId ? SearchMark.Current : SearchMark.Match;
    }

    /// <summary>Starts a search in the conversation. With <paramref name="focusMessageId"/> the first hit shown is that message.</summary>
    public void Open(long threadId, string? text, long focusMessageId)
    {
        Close();
        IsActive = true;
        _anchorRow = 0;

        _loading = Task.Run(async () =>
        {
            try
            {
                var rows = await smsService.GetThreadTextRowsAsync(threadId).ConfigureAwait(false);
                var index = new ThreadSearchIndex(rows);
                if (focusMessageId > 0)
                    _anchorRow = Math.Max(0, index.RowOf(focusMessageId));
                return index;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Reading the conversation for search failed: {ex}");
                return null;
            }
        });

        OnPropertyChanged(nameof(IsActive));
        _ = SetQueryAsync(text ?? string.Empty);
    }

    public void Close()
    {
        if (!IsActive)
            return;

        IsActive = false;
        _version++;
        _index = null;
        _loading = null;
        _hits = [];
        _hitIds = [];
        _current = -1;
        _query = string.Empty;
        OnPropertyChanged(nameof(IsActive));
        NotifyResultChanged();
        Changed?.Invoke();
    }

    public async Task SetQueryAsync(string text)
    {
        var clean = text.Trim();
        var version = ++_version;
        _query = clean;

        if (clean.Length == 0)
        {
            Apply(version, [], moveToHit: false);
            return;
        }

        var index = _index ?? await (_loading ?? Task.FromResult<ThreadSearchIndex?>(null));
        if (version != _version || index is null)
            return;

        _index = index;
        var hits = await Task.Run(() => index.Search(clean));
        if (version == _version)
            Apply(version, hits, moveToHit: true);
    }

    /// <summary>Moves to the next older hit, wrapping to the newest after the oldest.</summary>
    public void Older() => Move(+1);

    /// <summary>Moves to the next newer hit, wrapping to the oldest after the newest.</summary>
    public void Newer() => Move(-1);

    private void Move(int step)
    {
        if (_hits.Count == 0)
            return;

        _current = (_current + step + _hits.Count) % _hits.Count;
        _anchorRow = _hits[_current].Row;
        NotifyResultChanged();
        Changed?.Invoke();
        CurrentHitChanged?.Invoke(_hits[_current]);
    }

    private void Apply(int version, IReadOnlyList<ThreadSearchHit> hits, bool moveToHit)
    {
        if (version != _version)
            return;

        var previous = CurrentHit;
        _hits = hits;
        _hitIds = hits.Count == 0 ? [] : [.. hits.Select(h => h.MessageId)];
        _current = NearestToAnchor(hits);

        NotifyResultChanged();
        Changed?.Invoke();

        // Typing keeps the reader where they are when that message still matches; only a move needs a scroll.
        if (moveToHit && CurrentHit is { } hit && hit != previous)
        {
            _anchorRow = hit.Row;
            CurrentHitChanged?.Invoke(hit);
        }
    }

    // The hit closest to where the reader was: the message they tapped, the one they were on, or the newest.
    private int NearestToAnchor(IReadOnlyList<ThreadSearchHit> hits)
    {
        if (hits.Count == 0)
            return -1;

        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < hits.Count; i++)
        {
            var distance = Math.Abs(hits[i].Row - _anchorRow);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void NotifyResultChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasHits));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(CounterText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
