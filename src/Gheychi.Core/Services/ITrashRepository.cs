using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public interface ITrashRepository
{
    /// <summary>False when the messages could not be kept.</summary>
    Task<bool> AddAsync(IReadOnlyCollection<TrashedMessage> messages);

    /// <summary>Most recently deleted first.</summary>
    Task<IReadOnlyList<TrashedMessage>> GetAllAsync();

    Task DeleteAsync(IEnumerable<long> ids);

    Task DeleteAllAsync();

    /// <summary>Erases what was deleted before <paramref name="cutoff"/>.</summary>
    Task DeleteOlderThanAsync(DateTime cutoff);
}
