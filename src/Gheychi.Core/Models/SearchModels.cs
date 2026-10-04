namespace Gheychi.Core.Models;

public enum SearchFilterKind
{
    None,
    Unread,
    Starred,
    Known,
    Unknown,
    Sim
}

public sealed record SearchQuery(
    string? Text,
    SearchFilterKind FilterKind = SearchFilterKind.None,
    int? SimSlot = null,
    bool IncludeArchivedAndSpam = false
);

public sealed record SearchResultChat(
    long ThreadId,
    long MessageId,
    string Address,
    string? ContactName,
    string Snippet,
    DateTime Timestamp,
    int SubId,
    int TotalMatches,
    bool IsRead,
    bool IsArchived = false,
    bool IsSpam = false,
    bool IsStarred = false,
    bool IsKnown = false
);
