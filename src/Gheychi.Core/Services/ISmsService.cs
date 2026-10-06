using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public interface ISmsService
{
    Task<bool> EnsureDefaultSmsAppAsync();
    bool IsDefaultSmsApp();
    Task<bool> EnsurePermissionsAsync();
    Task<IReadOnlyList<SmsThread>> GetThreadsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, int? limit = null, int offset = 0, CancellationToken cancellationToken = default);
    /// <summary>Stores the message as outgoing before it is sent. Returns its row id, or 0 if it could not be stored.</summary>
    Task<long> QueueOutgoingAsync(string address, string text, int subId, CancellationToken cancellationToken = default);
    /// <summary>Sends the message. With a <paramref name="messageId"/> from <see cref="QueueOutgoingAsync"/> (or a failed message being retried) that row is updated in place; with 0 a row is created.</summary>
    Task<SmsSendResult> SendSmsAsync(string address, string text, int subId, long messageId = 0, CancellationToken cancellationToken = default);
    /// <summary>Puts a received message (e.g. one restored from spam) into the inbox as read. Returns its thread id, or 0 on failure.</summary>
    Task<long> RestoreIncomingAsync(string address, string body, DateTime timestamp, int subId);
    Task<bool> MarkThreadAsReadAsync(long threadId, CancellationToken cancellationToken = default);
    Task<bool> MarkThreadAsUnreadAsync(long threadId, CancellationToken cancellationToken = default);
    Task<bool> DeleteThreadsAsync(IReadOnlyList<long> threadIds, CancellationToken cancellationToken = default);
    Task<bool> DeleteMessagesAsync(IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SimCardInfo>> GetActiveSimsAsync();
    Task<IReadOnlyDictionary<int, int>> GetSimSlotMapAsync();
    Task<IReadOnlyDictionary<int, string>> GetSimCarrierMapAsync();
    Task<bool> IsDualSimAsync();
    /// <summary>Every phone number in the address book, sorted by name. Empty without contacts permission.</summary>
    Task<IReadOnlyList<ContactEntry>> GetContactsAsync(CancellationToken cancellationToken = default);
    /// <summary>Id of the conversation with <paramref name="address"/>, created when there is none yet; 0 on failure.</summary>
    Task<long> GetOrCreateThreadIdAsync(string address);
    Task<IReadOnlyList<SearchResultChat>> SearchChatsAsync(SearchQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResultLink>> SearchLinksAsync(SearchQuery query, CancellationToken cancellationToken = default);
    /// <summary>The text of every message in the conversation, newest first: the input of in-chat search and the links list.</summary>
    Task<IReadOnlyList<ThreadTextRow>> GetThreadTextRowsAsync(long threadId, CancellationToken cancellationToken = default);
}
