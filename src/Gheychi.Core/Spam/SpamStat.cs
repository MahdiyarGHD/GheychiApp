namespace Gheychi.Core.Spam;

/// <summary>
/// One detection, kept for the analytics after the message itself is cleared, deleted or restored.
/// <see cref="Restored"/> marks a false alarm: the user moved it back to the inbox.
/// </summary>
public sealed record SpamStat(long Id, long SpamMessageId, string SenderKey, DateTime Timestamp, float Score, int ModelVersion, bool Restored = false);
