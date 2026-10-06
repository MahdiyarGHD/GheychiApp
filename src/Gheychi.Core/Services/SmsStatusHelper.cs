namespace Gheychi.Core.Services;

public enum MessageInfoStatus
{
    Received,
    Sent,
    Delivered,
    Failed,
    Sending
}

public static class SmsStatusHelper
{
    public const int TypeInbox = 1;
    public const int TypeSent = 2;
    public const int TypeDraft = 3;
    public const int TypeOutbox = 4;
    public const int TypeFailed = 5;
    public const int TypeQueued = 6;

    public const int StatusNone = -1;
    public const int StatusComplete = 0;
    public const int StatusPending = 32;
    public const int StatusFailed = 64;

    public static bool IsOutgoingType(int type) =>
        type is TypeSent or TypeOutbox or TypeFailed or TypeQueued;

    public static bool HasFailed(int type, int status) =>
        type == TypeFailed || status == StatusFailed;

    /// <summary>The carrier accepted the message; it may or may not have reached the phone yet.</summary>
    public static bool IsSent(int type, int status) =>
        type == TypeSent && !HasFailed(type, status);

    /// <summary>
    /// A delivery report confirmed it (Complete), or it carries no delivery information at all (None: sent before
    /// delivery reports were requested, or by another app), which keeps the old double tick.
    /// </summary>
    public static bool IsDelivered(int type, int status) =>
        !HasFailed(type, status) &&
        (status == StatusComplete || (type == TypeSent && status == StatusNone));

    public static MessageInfoStatus GetInfoStatus(bool isOutgoing, bool hasFailed, bool isDelivered, bool isSending) =>
        !isOutgoing ? MessageInfoStatus.Received
        : hasFailed ? MessageInfoStatus.Failed
        : isDelivered ? MessageInfoStatus.Delivered
        : isSending ? MessageInfoStatus.Sending
        : MessageInfoStatus.Sent;
}
