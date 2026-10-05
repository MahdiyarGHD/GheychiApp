namespace Gheychi.Core.Notifications;

/// <summary>The open chat already shows the message, so a notification would only repeat it.</summary>
public sealed class ActiveChatRule : INotificationRule
{
    private readonly IActiveChatState _state;

    public ActiveChatRule(IActiveChatState state)
    {
        _state = state;
    }

    public NotificationDecision Evaluate(IncomingMessage message) =>
        _state.IsThreadOnScreen(message.ThreadId) ? NotificationDecision.Suppress : NotificationDecision.Show;
}
