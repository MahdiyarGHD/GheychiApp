namespace Gheychi.Core.Notifications;

public interface IActiveChatState
{
    /// <summary>True when the user is looking at this conversation right now.</summary>
    bool IsThreadOnScreen(long threadId);
}
