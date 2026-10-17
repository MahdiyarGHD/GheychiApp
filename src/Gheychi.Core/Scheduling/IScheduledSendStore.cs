namespace Gheychi.Core.Scheduling;

public interface IScheduledSendStore
{
    IReadOnlyList<ScheduledMessage> GetAll();

    IReadOnlyList<ScheduledMessage> ForThread(long threadId);

    void Add(ScheduledMessage message);

    /// <summary>
    /// Takes the message out and returns it, or null when it is already gone. Whoever gets it sends it: the in-app timer
    /// and the alarm both ask, and only one of them is handed the message, so it never goes twice. Undo and delete use it too.
    /// </summary>
    ScheduledMessage? Claim(string id);

    /// <summary>Raised after any change, from whichever thread made it.</summary>
    event Action? Changed;
}
