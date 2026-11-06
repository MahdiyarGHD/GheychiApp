namespace Gheychi.Core.Spam;

/// <summary>Quarantined spam. These messages are never written to the system SMS store.</summary>
public interface ISpamMessageRepository
{
    /// <summary>Returns the new row id, or 0 when the message could not be stored.</summary>
    Task<long> AddAsync(SpamMessage message);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<SpamMessage>> GetAllAsync();

    Task DeleteAsync(IEnumerable<long> ids);

    Task DeleteAllAsync();

    /// <summary>Messages caught on arrival before <paramref name="cutoff"/>. A scan's messages expire with their scan instead.</summary>
    Task DeleteOlderThanAsync(DateTime cutoff);

    /// <summary>Every message the given history scans moved here. False when they could not be deleted.</summary>
    Task<bool> DeleteScansAsync(IEnumerable<long> scanIds);
}
