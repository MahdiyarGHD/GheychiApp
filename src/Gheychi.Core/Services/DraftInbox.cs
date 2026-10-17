using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

/// <summary>Puts the unsent drafts into the list of conversations.</summary>
public static class DraftInbox
{
    /// <summary>
    /// A conversation with a draft shows the draft as its last line and sorts by the later of the draft and its newest
    /// message: a message that arrives after the draft goes above it, one before it stays below. A draft of a
    /// conversation with no messages yet is listed as a conversation of its own.
    /// </summary>
    public static IReadOnlyList<SmsThread> Merge(IReadOnlyList<SmsThread> threads, IReadOnlyCollection<MessageDraft> drafts)
    {
        var pending = new Dictionary<long, MessageDraft>();
        foreach (var draft in drafts)
        {
            if (!string.IsNullOrWhiteSpace(draft.Text) && (!pending.TryGetValue(draft.ThreadId, out var known) || draft.DateMs > known.DateMs))
                pending[draft.ThreadId] = draft;
        }

        if (pending.Count == 0)
            return threads;

        var merged = new List<SmsThread>(threads.Count + pending.Count);
        foreach (var thread in threads)
        {
            if (!pending.Remove(thread.ThreadId, out var draft))
            {
                merged.Add(thread);
                continue;
            }

            var date = DateOf(draft);
            merged.Add(thread with { Snippet = draft.Text, Timestamp = date > thread.Timestamp ? date : thread.Timestamp, HasDraft = true });
        }

        foreach (var draft in pending.Values)
            merged.Add(new SmsThread(draft.ThreadId, draft.Address, draft.Name, draft.Text, DateOf(draft), 0, 0, false, draft.SubId, HasDraft: true));

        // A stable sort: conversations of the same moment stay in the order the provider gave them.
        return merged.OrderByDescending(t => t.Timestamp).ToList();
    }

    private static DateTime DateOf(MessageDraft draft) => DateTimeOffset.FromUnixTimeMilliseconds(draft.DateMs).LocalDateTime;
}
