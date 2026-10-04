using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public interface IMessageMetadataRepository
{
    Task<MessageMetadata?> GetAsync(long messageId);
    Task<IReadOnlyDictionary<long, MessageMetadata>> GetForMessagesAsync(IEnumerable<long> messageIds);
    Task<IReadOnlyList<MessageMetadata>> GetStarredForThreadAsync(long threadId);
    Task<IReadOnlyList<long>> GetAllStarredMessageIdsAsync();
    Task SetStarredAsync(long messageId, long threadId, bool isStarred);
    Task SetReactionAsync(long messageId, long threadId, string? emoji, bool fromMe);
    Task DeleteAsync(IEnumerable<long> messageIds);
    Task DeleteForThreadsAsync(IEnumerable<long> threadIds);
}
