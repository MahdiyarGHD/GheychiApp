namespace Gheychi.Core.Models;

public sealed record SmsThread(
    long ThreadId,
    string Address,
    string? ContactName,
    string Snippet,
    DateTime Timestamp,
    int TotalCount,
    int UnreadCount,
    bool HasFailed,
    int SubId
);
