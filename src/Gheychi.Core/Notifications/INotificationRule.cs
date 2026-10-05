namespace Gheychi.Core.Notifications;

/// <summary>
/// One reason to not notify (or to notify quietly). Rules run on a background thread inside the
/// SMS receiver: keep them synchronous and cheap, with no UI access.
/// </summary>
public interface INotificationRule
{
    NotificationDecision Evaluate(IncomingMessage message);
}
