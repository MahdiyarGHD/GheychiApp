using Gheychi.Core.Models;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SmsMessagePageTests
{
    private static SmsMessage Msg(long id) =>
        new(id, 1, "+1", "x", DateTime.Now, false, false, false, 1);

    [Fact]
    public void RawCount_SurvivesFilteringOutReactionRows()
    {
        var page = new SmsMessagePage([Msg(1), Msg(2)], rawCount: 3);

        Assert.Equal(2, page.Count);
        Assert.Equal(3, SmsMessagePage.RawCountOf(page));
    }

    [Fact]
    public void RawCountOf_PlainList_IsItsCount()
    {
        IReadOnlyList<SmsMessage> list = [Msg(1), Msg(2)];

        Assert.Equal(2, SmsMessagePage.RawCountOf(list));
    }
}
