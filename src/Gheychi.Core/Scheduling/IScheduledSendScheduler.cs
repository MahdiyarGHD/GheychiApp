namespace Gheychi.Core.Scheduling;

/// <summary>Wakes the app at the time a message is due, even when the process is gone.</summary>
public interface IScheduledSendScheduler
{
    /// <summary>True when the system lets the app fire at the exact time; otherwise it fires a little late, in the system's own time.</summary>
    bool CanFireExactly { get; }

    void Schedule(ScheduledMessage message);

    void Cancel(string id);
}
