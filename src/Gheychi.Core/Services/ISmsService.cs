using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public interface ISmsService
{
    Task<bool> EnsureDefaultSmsAppAsync();
    Task<bool> EnsurePermissionsAsync();
    Task<IReadOnlyList<SmsThread>> GetThreadsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, int? limit = null, int offset = 0, CancellationToken cancellationToken = default);
    Task<bool> SendSmsAsync(string address, string text, int subId, CancellationToken cancellationToken = default);
    Task<bool> MarkThreadAsReadAsync(long threadId, CancellationToken cancellationToken = default);
    Task<bool> MarkThreadAsUnreadAsync(long threadId, CancellationToken cancellationToken = default);
    Task<bool> DeleteThreadsAsync(IReadOnlyList<long> threadIds, CancellationToken cancellationToken = default);
    Task<bool> DeleteMessagesAsync(IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SimCardInfo>> GetActiveSimsAsync();
    Task<IReadOnlyDictionary<int, int>> GetSimSlotMapAsync();
    Task<IReadOnlyDictionary<int, string>> GetSimCarrierMapAsync();
    Task<bool> IsDualSimAsync();
    Task<IReadOnlyList<SearchResultChat>> SearchChatsAsync(SearchQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResultLink>> SearchLinksAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
