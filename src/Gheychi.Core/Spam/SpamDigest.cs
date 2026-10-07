using Gheychi.Core.Services;

namespace Gheychi.Core.Spam;

/// <summary>What the daily spam summary says: how much was caught and from whom.</summary>
/// <param name="TopSenders">Display forms of the senders with the most messages, most first.</param>
/// <param name="OtherSenders">Senders beyond <paramref name="TopSenders"/>.</param>
/// <param name="AllToday">Every counted message arrived on the summary's own day, so it can say "today".</param>
public sealed record SpamDigest(int Count, IReadOnlyList<string> TopSenders, int OtherSenders, bool AllToday);

public static class SpamDigests
{
    public const int DefaultMinuteOfDay = 21 * 60;

    /// <summary>A summary never reaches further back than this, however long ago the last one ran.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(1);

    /// <summary>A summary missed while the phone was off still runs if the phone comes back within this.</summary>
    public static readonly TimeSpan CatchUp = TimeSpan.FromHours(3);

    public static DateTime WindowStart(DateTime now, DateTime? lastRun) =>
        lastRun is { } last && last > now - MaxWindow ? last : now - MaxWindow;

    /// <summary>
    /// When the summary should run next: <paramref name="now"/> when today's (or last night's) run was missed a short
    /// while ago, otherwise the next time the clock reaches <paramref name="minuteOfDay"/>.
    /// </summary>
    public static DateTime NextRun(DateTime now, int minuteOfDay, DateTime? lastRun)
    {
        var slot = now.Date.AddMinutes(Math.Clamp(minuteOfDay, 0, 24 * 60 - 1));
        if (slot > now)
            slot = slot.AddDays(-1);

        if (lastRun is { } last && last < slot && now - slot <= CatchUp)
            return now;

        return slot.AddDays(1);
    }

    /// <summary>
    /// The summary of the spam caught after <paramref name="since"/>; null when there is none, or when the user has
    /// already opened the Spam tab (<paramref name="seenAt"/>) since the newest of it arrived.
    /// </summary>
    public static SpamDigest? Compose(IEnumerable<SpamMessage> messages, DateTime since, DateTime now, DateTime? seenAt, int maxSenders = 2)
    {
        var caught = messages.Where(m => m.Timestamp > since && m.Timestamp <= now).ToList();
        if (caught.Count == 0 || seenAt >= caught.Max(m => m.Timestamp))
            return null;

        var senders = caught
            .GroupBy(m => PhoneNumberNormalizer.ToLookupKey(m.Address))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(m => m.Timestamp))
            .Select(g => PhoneNumberNormalizer.FormatDisplay(g.OrderByDescending(m => m.Timestamp).First().Address))
            .ToList();

        var top = senders.Take(maxSenders).ToList();
        return new SpamDigest(caught.Count, top, senders.Count - top.Count, caught.All(m => m.Timestamp.Date == now.Date));
    }
}
