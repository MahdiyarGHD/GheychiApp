using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

/// <summary>What a deleted message is kept for, and how the trash lists it.</summary>
public static class TrashPolicy
{
    public const int RetentionDays = 30;

    public static DateTime Cutoff(DateTime now) => now.AddDays(-RetentionDays);

    /// <summary>Whole days left before the messages are erased; never below 1 while they are still kept.</summary>
    public static int DaysLeft(long trashedAtMs, DateTime now)
    {
        var erasedAt = DateTimeOffset.FromUnixTimeMilliseconds(trashedAtMs).LocalDateTime.AddDays(RetentionDays);
        return Math.Max(1, (int)Math.Ceiling((erasedAt - now).TotalDays));
    }

    /// <summary>
    /// The type a message comes back with. One that was still waiting to go out would be sent by nobody now: it returns as
    /// failed, so it shows its retry instead of looking sent.
    /// </summary>
    public static int RestoredType(int type) =>
        type is SmsStatusHelper.TypeOutbox or SmsStatusHelper.TypeQueued or SmsStatusHelper.TypeDraft
            ? SmsStatusHelper.TypeFailed
            : type;

    /// <summary>One entry per sender and deletion: a whole conversation deleted at once is a single item, not hundreds.</summary>
    public static IReadOnlyList<TrashGroup> Group(IEnumerable<TrashedMessage> messages) =>
        messages
            .GroupBy(m => (Key: PhoneNumberNormalizer.ToLookupKey(m.Address), m.TrashedAtMs))
            .Select(g => new TrashGroup(ConversationAddress(g), g.Key.TrashedAtMs, g.OrderByDescending(m => m.DateMs).ToList()))
            .OrderByDescending(g => g.TrashedAtMs)
            .ToList();

    /// <summary>
    /// The address each conversation is put back under, by <see cref="PhoneNumberNormalizer.ToLookupKey"/>. One person's
    /// number is often written two ways in the same chat (the sent messages as +98..., the received ones as the carrier
    /// wrote it); the received form is the one their next message will come from.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ConversationAddresses(IEnumerable<TrashedMessage> messages) =>
        messages
            .GroupBy(m => PhoneNumberNormalizer.ToLookupKey(m.Address))
            .ToDictionary(g => g.Key, ConversationAddress);

    private static string ConversationAddress(IEnumerable<TrashedMessage> messages) =>
        (messages.FirstOrDefault(m => m.Type == SmsStatusHelper.TypeInbox) ?? messages.First()).Address;
}

/// <summary>Messages of one sender deleted together; <see cref="Messages"/> is newest first.</summary>
public sealed record TrashGroup(string Address, long TrashedAtMs, IReadOnlyList<TrashedMessage> Messages)
{
    public TrashedMessage Newest => Messages[0];

    public IEnumerable<long> Ids => Messages.Select(m => m.Id);
}
