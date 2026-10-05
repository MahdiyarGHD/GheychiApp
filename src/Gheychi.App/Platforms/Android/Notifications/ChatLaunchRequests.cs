using Android.Content;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// Everything needed to show a conversation without first loading the inbox: a tapped notification
/// already knows who the chat is with.
/// </summary>
public sealed record ChatLaunchRequest(long ThreadId, string Address, string Name, int SubId)
{
    /// <summary>Completes with the unread count once the conversation's messages are read ahead.</summary>
    public Task<int> Prepared { get; init; } = Task.FromResult(0);
}

/// <summary>
/// A tapped notification asks the UI to open a conversation. The request is kept until the inbox
/// takes it: on a cold start the activity receives the intent before any page exists, and from the
/// background it arrives before the activity is visible, when opening a chat would fight the resume.
/// </summary>
public static class ChatLaunchRequests
{
    private static ChatLaunchRequest? _pending;

    public static event Action? Requested;

    public static bool HasPending => Volatile.Read(ref _pending) is not null;

    public static void FromIntent(Intent? intent)
    {
        var threadId = intent?.GetLongExtra(NotificationIds.ExtraThreadId, 0) ?? 0;
        if (threadId <= 0)
            return;

        var request = new ChatLaunchRequest(
            threadId,
            intent!.GetStringExtra(NotificationIds.ExtraAddress) ?? string.Empty,
            intent.GetStringExtra(NotificationIds.ExtraName) ?? string.Empty,
            intent.GetIntExtra(NotificationIds.ExtraSubId, -1));
        request = request with { Prepared = ChatViewModel.PrepareLaunchAsync(request) };

        // The intent is delivered again after the activity is restored; it must not reopen the chat.
        intent.RemoveExtra(NotificationIds.ExtraThreadId);
        Volatile.Write(ref _pending, request);

        if (ChatPresence.IsAppVisible)
            Requested?.Invoke();
    }

    /// <summary>Called once the activity is visible again, so a request that arrived on the way is acted on.</summary>
    public static void RaiseIfPending()
    {
        if (Volatile.Read(ref _pending) is not null)
            Requested?.Invoke();
    }

    /// <summary>The pending request, if any; each request is handed out once.</summary>
    public static ChatLaunchRequest? Take() => Interlocked.Exchange(ref _pending, null);
}
