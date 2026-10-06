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
    float Threshold = 0
);
