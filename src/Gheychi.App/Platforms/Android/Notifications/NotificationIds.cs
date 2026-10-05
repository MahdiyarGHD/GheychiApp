namespace Gheychi.App.Platforms.Android.Notifications;

internal static class NotificationIds
{
    public const string ChannelMessages = "messages";
    public const string GroupKey = "com.evergreen.gheychiapp.MESSAGES";
    public const int SummaryId = 1;

    public const string ExtraThreadId = "com.evergreen.gheychiapp.THREAD_ID";
    public const string ExtraAddress = "com.evergreen.gheychiapp.ADDRESS";
    public const string ExtraSubId = "com.evergreen.gheychiapp.SUB_ID";

    private const int ThreadIdBase = 1000;
    private const int ThreadIdRange = 100_000_000;

    public static int ForThread(long threadId) => ThreadIdBase + (int)(threadId % ThreadIdRange);

    /// <summary>Request codes must differ per thread and per action, or the PendingIntents overwrite each other.</summary>
    public static int RequestCode(long threadId, PendingSlot slot) => ForThread(threadId) * 4 + (int)slot;

    public enum PendingSlot
    {
        Open = 0,
        MarkRead = 1,
        Reply = 2
    }
}
