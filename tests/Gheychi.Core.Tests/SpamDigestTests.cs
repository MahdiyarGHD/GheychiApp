using Gheychi.Core.Spam;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SpamDigestTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 21, 0, 0);

    private static SpamMessage Spam(DateTime at, string address = "+989151112233") =>
        new(0, address, "win a prize", at, 1, 0.95f, 1);

    [Fact]
    public void NextRun_BeforeTodaysTime_IsToday()
    {
        var now = new DateTime(2026, 10, 7, 20, 0, 0);
        Assert.Equal(new DateTime(2026, 10, 7, 21, 0, 0), SpamDigests.NextRun(now, 21 * 60, lastRun: now.AddHours(-23)));
    }

    [Fact]
    public void NextRun_AfterTodaysRun_IsTomorrow()
    {
        var now = new DateTime(2026, 10, 7, 21, 0, 1);
        Assert.Equal(new DateTime(2026, 10, 8, 21, 0, 0), SpamDigests.NextRun(now, 21 * 60, lastRun: now));
    }

    [Fact]
    public void NextRun_MissedShortlyAgo_IsNow()
    {
        var now = new DateTime(2026, 10, 7, 22, 30, 0);
        Assert.Equal(now, SpamDigests.NextRun(now, 21 * 60, lastRun: new DateTime(2026, 10, 6, 21, 0, 0)));
    }

    [Fact]
    public void NextRun_MissedLongAgo_WaitsForTomorrow()
    {
        var now = new DateTime(2026, 10, 8, 8, 0, 0);
        Assert.Equal(new DateTime(2026, 10, 8, 21, 0, 0), SpamDigests.NextRun(now, 21 * 60, lastRun: new DateTime(2026, 10, 6, 21, 0, 0)));
    }

    [Fact]
    public void NextRun_FirstEver_NeverCatchesUp()
    {
        var now = new DateTime(2026, 10, 7, 21, 30, 0);
        Assert.Equal(new DateTime(2026, 10, 8, 21, 0, 0), SpamDigests.NextRun(now, 21 * 60, lastRun: null));
    }

    [Fact]
    public void WindowStart_IsLastRun_CappedAtOneDay()
    {
        Assert.Equal(Now.AddHours(-5), SpamDigests.WindowStart(Now, Now.AddHours(-5)));
        Assert.Equal(Now.AddDays(-1), SpamDigests.WindowStart(Now, Now.AddDays(-4)));
        Assert.Equal(Now.AddDays(-1), SpamDigests.WindowStart(Now, null));
    }

    [Fact]
    public void Compose_NothingInWindow_IsNull()
    {
        Assert.Null(SpamDigests.Compose([Spam(Now.AddDays(-2))], Now.AddDays(-1), Now, seenAt: null));
    }

    [Fact]
    public void Compose_AlreadySeen_IsNull()
    {
        Assert.Null(SpamDigests.Compose([Spam(Now.AddHours(-3))], Now.AddDays(-1), Now, seenAt: Now.AddHours(-1)));
    }

    [Fact]
    public void Compose_SomethingNewSinceSeen_CountsTheWholeWindow()
    {
        var digest = SpamDigests.Compose([Spam(Now.AddHours(-1)), Spam(Now.AddHours(-5))], Now.AddDays(-1), Now, seenAt: Now.AddHours(-2));
        Assert.Equal(2, digest!.Count);
    }

    [Fact]
    public void Compose_RanksSendersByCount_AndCountsTheRest()
    {
        var digest = SpamDigests.Compose(
        [
            Spam(Now.AddHours(-1), "Bank"),
            Spam(Now.AddHours(-2), "+989151112233"),
            Spam(Now.AddHours(-3), "09151112233"),
            Spam(Now.AddHours(-4), "Shop"),
            Spam(Now.AddHours(-5), "Promo")
        ], Now.AddDays(-1), Now, seenAt: null);

        Assert.Equal(5, digest!.Count);
        Assert.Equal(["+98 915 111 2233", "Bank"], digest.TopSenders);
        Assert.Equal(2, digest.OtherSenders);
        Assert.True(digest.AllToday);
    }

    [Fact]
    public void Compose_FromYesterday_IsNotToday()
    {
        var digest = SpamDigests.Compose([Spam(Now.AddHours(-23))], Now.AddDays(-1), Now, seenAt: null);
        Assert.False(digest!.AllToday);
    }
}
