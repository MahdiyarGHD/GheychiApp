using Android.Content;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// A tapped notification asks the UI to open a conversation. The request is kept until the inbox
/// takes it, because on a cold start the activity receives the intent before any page exists.
/// </summary>
public static class ChatLaunchRequests
{
    private static long _pendingThreadId;

    public static event Action? Requested;

    public static void FromIntent(Intent? intent)
    {
        var threadId = intent?.GetLongExtra(NotificationIds.ExtraThreadId, 0) ?? 0;
        if (threadId <= 0)
            return;

        // The intent is delivered again after the activity is restored; it must not reopen the chat.
        intent!.RemoveExtra(NotificationIds.ExtraThreadId);
        Interlocked.Exchange(ref _pendingThreadId, threadId);
        Requested?.Invoke();
    }

    /// <summary>The requested thread id, or 0; each request is handed out once.</summary>
    public static long Take() => Interlocked.Exchange(ref _pendingThreadId, 0);
}
