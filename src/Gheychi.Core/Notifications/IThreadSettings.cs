namespace Gheychi.Core.Notifications;

/// <summary>Per-conversation choices the user makes on the profile page.</summary>
public interface IThreadSettings
{
    /// <summary>Unix milliseconds until which the conversation is snoozed; 0 when it is not.</summary>
    long GetSnoozedUntil(long threadId);

    /// <summary>Snoozes until <paramref name="untilMillis"/>; 0 ends the snooze.</summary>
    void SetSnoozedUntil(long threadId, long untilMillis);

    /// <summary>The SIM (subscription id) always used to message this conversation; 0 when none is chosen.</summary>
    int GetPreferredSubId(long threadId);

    void SetPreferredSubId(long threadId, int subId);
}
