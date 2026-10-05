namespace Gheychi.Core.Notifications;

/// <summary>Written from the UI thread, read from the SMS receiver's background thread.</summary>
public sealed class ActiveChatState : IActiveChatState
{
    private long _openThreadId;
    private int _appVisible;

    public bool IsAppVisible => Volatile.Read(ref _appVisible) == 1;

    public bool IsThreadOnScreen(long threadId) =>
        threadId > 0
        && IsAppVisible
        && Interlocked.Read(ref _openThreadId) == threadId;

    public void SetOpenThread(long threadId) => Interlocked.Exchange(ref _openThreadId, threadId);

    public void ClearOpenThread() => Interlocked.Exchange(ref _openThreadId, 0);

    public void SetAppVisible(bool visible) => Volatile.Write(ref _appVisible, visible ? 1 : 0);
}
