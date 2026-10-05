using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

/// <summary>A message that contains the searched text. <see cref="Row"/> is its offset from the newest message.</summary>
public readonly record struct ThreadSearchHit(int Row, long MessageId);

/// <summary>
/// Searches one conversation entirely in memory. The rows are read once; every keystroke after that is a
/// scan of message bodies, and typing more of the same text only re-checks the previous hits.
/// </summary>
public sealed class ThreadSearchIndex(IReadOnlyList<ThreadTextRow> rows)
{
    private readonly object _gate = new();
    private string _lastText = string.Empty;
    private ThreadSearchHit[] _lastHits = [];

    public int RowCount => rows.Count;

    /// <summary>The offset from the newest message of the message with this id, or -1.</summary>
    public int RowOf(long messageId)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id == messageId)
                return i;
        }

        return -1;
    }

    /// <summary>Messages containing <paramref name="text"/>, newest first. Reaction messages are not messages in the chat and are skipped.</summary>
    public IReadOnlyList<ThreadSearchHit> Search(string? text)
    {
        // Typing can start a search before the previous one finished; they share the narrowing state.
        lock (_gate)
            return SearchLocked(text);
    }

    private IReadOnlyList<ThreadSearchHit> SearchLocked(string? text)
    {
        var clean = text?.Trim() ?? string.Empty;
        if (clean.Length == 0)
        {
            _lastText = string.Empty;
            _lastHits = [];
            return _lastHits;
        }

        var variants = SearchTextHelper.BuildVariants(clean);
        var narrowing = _lastText.Length > 0 && clean.Contains(_lastText, StringComparison.OrdinalIgnoreCase);

        var hits = new List<ThreadSearchHit>();
        if (narrowing)
        {
            foreach (var previous in _lastHits)
            {
                if (SearchTextHelper.ContainsAny(rows[previous.Row].Body, variants))
                    hits.Add(previous);
            }
        }
        else
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (SearchTextHelper.ContainsAny(row.Body, variants) && !ReactionHelper.TryParseReaction(row.Body).IsReaction)
                    hits.Add(new ThreadSearchHit(i, row.Id));
            }
        }

        _lastText = clean;
        _lastHits = [.. hits];
        return _lastHits;
    }
}
