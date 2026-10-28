using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SmsPartTallyTests
{
    [Fact]
    public void SinglePart_CompletesOnFirstResult()
    {
        var tally = new SmsPartTally(1);

        Assert.True(tally.Record(true));
        Assert.True(tally.AllOk);
    }

    [Fact]
    public void Multipart_WaitsForEveryPart()
    {
        var tally = new SmsPartTally(3);

        Assert.False(tally.Record(true));
        Assert.False(tally.Record(true));
        Assert.False(tally.IsComplete);
        Assert.True(tally.Record(true));
        Assert.True(tally.AllOk);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Multipart_AnyFailedPartFailsTheMessage(int failingPart)
    {
        var tally = new SmsPartTally(3);

        for (var i = 0; i < 3; i++)
            tally.Record(i != failingPart);

        Assert.True(tally.IsComplete);
        Assert.False(tally.AllOk);
    }

    [Fact]
    public void NoParts_CountsAsOne()
    {
        var tally = new SmsPartTally(0);

        Assert.True(tally.Record(true));
    }
}
