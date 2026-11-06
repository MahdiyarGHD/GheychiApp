namespace Gheychi.Core.Spam;

public sealed record SpamMessage(
    long Id,
    string Address,
    string Body,
    DateTime Timestamp,
    int SubId,
    float Score,
    int ModelVersion,
    // The threshold in force when the message was judged; 0 for messages stored before it was recorded.
    float Threshold = 0,
    // The history scan that moved it here; 0 for a message caught as it arrived.
    long ScanId = 0,
    // Only a scanned message can be unread: one caught on arrival never reached the inbox.
    bool Unread = false
);
