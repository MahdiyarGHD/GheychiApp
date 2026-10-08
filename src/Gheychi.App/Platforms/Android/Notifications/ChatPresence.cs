using Gheychi.Core.Notifications;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>What the UI tells the notification rules: which conversation is on screen, and whether the app is.</summary>
public static class ChatPresence
{
    private static ActiveChatState? State =>
        IPlatformApplication.Current?.Services.GetService<ActiveChatState>();

    public static bool IsAppVisible => State?.IsAppVisible ?? false;

    public static void ChatOpened(long threadId)
    {
        State?.SetOpenThread(threadId);

        // What was notified is being read now. Asking the notification service is a binder call that can
        // take tens of milliseconds, so it must not sit on the chat's opening path.
        _ = Task.Run(() => MessageNotifier.Cancel(Platform.AppContext, threadId));
    }

    public static void ChatClosed() => State?.ClearOpenThread();

    public static void AppVisibilityChanged(bool visible) => State?.SetAppVisible(visible);
}
