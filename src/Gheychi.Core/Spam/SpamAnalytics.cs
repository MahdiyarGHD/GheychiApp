namespace Gheychi.Core.Spam;

public readonly record struct SenderCount(string SenderKey, int Count);

public sealed record SpamAnalytics(
    int Total,
    int Today,
    int Last7Days,
    int Last30Days,
    int VeryLikely,
    int Likely,
    int Restored,
    IReadOnlyList<int> Daily,
    IReadOnlyList<SenderCount> TopSenders,
    int? BusiestHour,
    DateTime? Since)
{
    public const int DailyDays = 14;
    public const int TopSenderCount = 5;

    public static readonly SpamAnalytics Empty = Compute([], DateTime.Now);

    /// <summary>Share of detections the user did not restore, 0..100; null before the first one.</summary>
    public int? AccuracyPercent => Total == 0 ? null : (int)Math.Round((Total - Restored) * 100.0 / Total);

    /// <summary>Counts by calendar day in local time; <see cref="Daily"/> runs oldest first and ends today.</summary>
    public static SpamAnalytics Compute(IReadOnlyCollection<SpamStat> stats, DateTime now)
    {
        var today = now.Date;
        int CountFrom(DateTime start) => stats.Count(s => s.Timestamp >= start);

        var daily = new int[DailyDays];
        foreach (var stat in stats)
        {
            var daysAgo = (today - stat.Timestamp.Date).Days;
            if (daysAgo is >= 0 and < DailyDays)
                daily[DailyDays - 1 - daysAgo]++;
        }

        var topSenders = stats
            .Where(s => s.SenderKey.Length > 0)
            .GroupBy(s => s.SenderKey)
            .Select(g => new SenderCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.SenderKey, StringComparer.Ordinal)
            .Take(TopSenderCount)
            .ToList();

        int? busiestHour = stats.Count == 0
            ? null
            : stats.GroupBy(s => s.Timestamp.Hour).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;

        var veryLikely = stats.Count(s => SpamConfidence.Level(s.Score) == SpamConfidenceLevel.VeryLikely);

        return new SpamAnalytics(
            stats.Count,
            CountFrom(today),
            CountFrom(today.AddDays(-6)),
            CountFrom(today.AddDays(-29)),
            veryLikely,
            stats.Count - veryLikely,
            stats.Count(s => s.Restored),
            daily,
            topSenders,
            busiestHour,
            stats.Count == 0 ? null : stats.Min(s => s.Timestamp));
    }
}
