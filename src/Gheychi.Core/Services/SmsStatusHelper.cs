namespace Gheychi.Core.Services;

public enum MessageInfoStatus
{
    Received,
    Sent,
    Delivered,
    Failed,
    Sending,
    Unconfirmed
}

/// <summary>What a carrier's delivery report says about the message.</summary>
public enum DeliveryOutcome
{
    /// <summary>The message reached the phone.</summary>
    Delivered,

    /// <summary>The carrier is still trying, or the report says nothing usable: a final report may follow.</summary>
    Pending,

    /// <summary>The carrier gave up.</summary>
    Failed
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

    /// <summary>How long an outgoing message may wait for the radio's answer before its outcome counts as unknown.</summary>
    public const long UnconfirmedAfterMs = 10 * 60 * 1000;

    public static bool HasFailed(int type, int status) =>
        type == TypeFailed || status == StatusFailed;

    /// <summary>
    /// Still in the outbox long after it was handed to the radio: the result broadcast never came (the phone restarted
    /// mid-send, the radio dropped it). It may or may not have gone out, so it is neither sent nor failed, and sending it
    /// again can deliver it twice.
    /// </summary>
    public static bool IsUnconfirmed(int type, long dateMs, long nowMs) =>
        type == TypeOutbox && nowMs - dateMs > UnconfirmedAfterMs;

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

    /// <summary>
    /// Reads the TP-Status of a status report (3GPP TS 23.040): below 32 the message reached the phone, 32-63 the carrier
    /// is still trying, 64 and above it gave up (permanently, or after it stopped trying).
    /// </summary>
    public static DeliveryOutcome ClassifyReport(int tpStatus) =>
        tpStatus < 0 ? DeliveryOutcome.Pending
        : tpStatus < StatusPending ? DeliveryOutcome.Delivered
        : tpStatus < StatusFailed ? DeliveryOutcome.Pending
        : DeliveryOutcome.Failed;

    public static MessageInfoStatus GetInfoStatus(bool isOutgoing, bool hasFailed, bool isDelivered, bool isSending, bool isUnconfirmed = false) =>
        !isOutgoing ? MessageInfoStatus.Received
        : hasFailed ? MessageInfoStatus.Failed
        : isDelivered ? MessageInfoStatus.Delivered
        : isUnconfirmed ? MessageInfoStatus.Unconfirmed
        : isSending ? MessageInfoStatus.Sending
        : MessageInfoStatus.Sent;
}
