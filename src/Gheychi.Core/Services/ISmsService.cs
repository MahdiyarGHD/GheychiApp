using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public interface ISmsService
{
    Task<bool> EnsureDefaultSmsAppAsync();
    Task<bool> EnsurePermissionsAsync();
    Task<IReadOnlyList<SmsThread>> GetThreadsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, int? limit = null, int offset = 0, CancellationToken cancellationToken = default);
    /// <summary>Stores the message as outgoing before it is sent. Returns its row id, or 0 if it could not be stored.</summary>
    Task<long> QueueOutgoingAsync(string address, string text, int subId, CancellationToken cancellationToken = default);
    /// <summary>Sends the message. With a <paramref name="messageId"/> from <see cref="QueueOutgoingAsync"/> (or a failed message being retried) that row is updated in place; with 0 a row is created.</summary>
    Task<SmsSendResult> SendSmsAsync(string address, string text, int subId, long messageId = 0, CancellationToken cancellationToken = default);
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
