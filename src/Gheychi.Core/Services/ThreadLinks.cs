using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

/// <summary>A link found in a conversation, with the message it came from.</summary>
public sealed record ThreadLink(long MessageId, long DateMs, DetectedItem Item);

public static class ThreadLinks
{
    /// <summary>Every link in the conversation, newest first (the rows are newest first already).</summary>
    public static IReadOnlyList<ThreadLink> Find(IReadOnlyList<ThreadTextRow> rows)
    {
        var links = new List<ThreadLink>();
        foreach (var row in rows)
        {
            // Cheap check first: almost every message has no link at all.
            if (!row.Body.Contains("http", StringComparison.OrdinalIgnoreCase) &&
                !row.Body.Contains("www.", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var item in LinkExtractor.Find(row.Body))
                links.Add(new ThreadLink(row.Id, row.DateMs, item));
        }

        return links;
    }
}
