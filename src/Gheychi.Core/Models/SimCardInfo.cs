namespace Gheychi.Core.Models;

public sealed record SimCardInfo(
    int SlotIndex,
    int SubId,
    string DisplayName
);
