using Gheychi.Core.Notifications;
using Microsoft.Maui.ApplicationModel;

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
        // What was notified is being read now.
        MessageNotifier.Cancel(Platform.AppContext, threadId);
    }

    public static void ChatClosed() => State?.ClearOpenThread();

    public static void AppVisibilityChanged(bool visible) => State?.SetAppVisible(visible);
}
