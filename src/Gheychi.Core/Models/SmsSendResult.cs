namespace Gheychi.Core.Models;

/// <summary>Pending: no result yet (slow network). The message may still go out, so it is not a failure.</summary>
public enum SmsSendResult
{
    Sent,
    Failed,
    Pending
}
