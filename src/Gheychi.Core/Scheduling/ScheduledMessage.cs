namespace Gheychi.Core.Scheduling;

/// <summary>
/// A message waiting to go out: one scheduled for a time the user picked, or one held for the send delay so it can
/// still be undone. Kept apart from the SMS store until it is sent, so other apps never see an unsent message.
/// </summary>
/// <param name="IsDelay">True for a message held by the send delay, false for one scheduled for a chosen time.</param>
public sealed record ScheduledMessage(
    string Id,
    long ThreadId,
    string Address,
    string Text,
    int SubId,
    long SendAtMillis,
    bool IsDelay);
