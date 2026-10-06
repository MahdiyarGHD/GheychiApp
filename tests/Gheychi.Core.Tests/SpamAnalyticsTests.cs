using Gheychi.Core.Spam;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SpamAnalyticsTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 15, 0, 0);

    private static SpamStat Stat(DateTime at, string sender = "Bank", float score = 0.9f, bool restored = false) =>
        new(0, 0, sender, at, score, 1, restored);

    [Fact]
    public void Empty_HasNothing()
    {
        var analytics = SpamAnalytics.Compute([], Now);

        Assert.Equal(0, analytics.Total);
        Assert.Null(analytics.AccuracyPercent);
        Assert.Null(analytics.BusiestHour);
        Assert.Null(analytics.Since);
        Assert.Equal(SpamAnalytics.DailyDays, analytics.Daily.Count);
        Assert.All(analytics.Daily, d => Assert.Equal(0, d));
    }

    [Fact]
    public void Windows_CountCalendarDays()
    {
        var analytics = SpamAnalytics.Compute(
        [
            Stat(Now.Date.AddHours(1)),
            Stat(Now.Date.AddDays(-6).AddHours(23)),
            Stat(Now.Date.AddDays(-7)),
            Stat(Now.Date.AddDays(-29)),
            Stat(Now.Date.AddDays(-30))
        ], Now);

        Assert.Equal(5, analytics.Total);
        Assert.Equal(1, analytics.Today);
        Assert.Equal(2, analytics.Last7Days);
        Assert.Equal(4, analytics.Last30Days);
        Assert.Equal(Now.Date.AddDays(-30), analytics.Since);
    }

    [Fact]
    public void Daily_EndsToday()
    {
        var analytics = SpamAnalytics.Compute([Stat(Now), Stat(Now.AddHours(-1)), Stat(Now.AddDays(-13)), Stat(Now.AddDays(-14))], Now);

        Assert.Equal(2, analytics.Daily[^1]);
        Assert.Equal(1, analytics.Daily[0]);
        Assert.Equal(3, analytics.Daily.Sum());
    }

    [Fact]
    public void Splits_ConfidenceAndFalseAlarms()
    {
        var analytics = SpamAnalytics.Compute(
        [
            Stat(Now, score: 0.99f),
            Stat(Now, score: 0.95f),
            Stat(Now, score: 0.9f, restored: true),
            Stat(Now, score: 0.86f)
        ], Now);

        Assert.Equal(2, analytics.VeryLikely);
        Assert.Equal(2, analytics.Likely);
        Assert.Equal(1, analytics.Restored);
        Assert.Equal(75, analytics.AccuracyPercent);
    }

    [Fact]
    public void TopSenders_AndBusiestHour()
    {
        var analytics = SpamAnalytics.Compute(
        [
            Stat(Now.Date.AddHours(9), "Shop"),
            Stat(Now.Date.AddHours(9).AddMinutes(30), "Shop"),
            Stat(Now.Date.AddHours(20), "Bank"),
            Stat(Now.Date.AddHours(9).AddMinutes(45), "Shop")
        ], Now);

        Assert.Equal([new SenderCount("Shop", 3), new SenderCount("Bank", 1)], analytics.TopSenders);
        Assert.Equal(9, analytics.BusiestHour);
    }
}
