namespace Gheychi.Core.Notifications;

/// <summary>Ordered from most to least permissive; when rules disagree the larger value wins.</summary>
public enum NotificationDecision
{
    Show = 0,
    Silent = 1,
    Suppress = 2
}
