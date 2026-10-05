namespace Gheychi.Core.Notifications;

/// <summary>A snoozed conversation stays quiet: the message is stored and counted as unread, but nothing is posted.</summary>
public sealed class SnoozeRule(IThreadSettings settings, TimeProvider? time = null) : INotificationRule
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public NotificationDecision Evaluate(IncomingMessage message) =>
        SnoozeSchedule.IsActive(settings.GetSnoozedUntil(message.ThreadId), _time.GetUtcNow().ToUnixTimeMilliseconds())
            ? NotificationDecision.Suppress
            : NotificationDecision.Show;
}
