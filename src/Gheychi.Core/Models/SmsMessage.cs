namespace Gheychi.Core.Models;

public sealed record SmsMessage(
    long Id,
    long ThreadId,
    string Address,
    string Body,
    DateTime Timestamp,
    bool IsOutgoing,
    bool IsDelivered,
    bool HasFailed,
    int SubId,
    bool IsRead = true,
    bool IsStarred = false,
    string? Reaction = null
);
