namespace Gheychi.Core.Spam;

/// <summary>Quarantined spam. These messages are never written to the system SMS store.</summary>
public interface ISpamMessageRepository
{
    /// <summary>Returns the new row id, or 0 when the message could not be stored.</summary>
    Task<long> AddAsync(SpamMessage message);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<SpamMessage>> GetAllAsync();
}
