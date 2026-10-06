namespace Gheychi.Core.Spam;

public sealed record SpamMessage(
    long Id,
    string Address,
    string Body,
    DateTime Timestamp,
    int SubId,
    float Score,
    int ModelVersion
);
