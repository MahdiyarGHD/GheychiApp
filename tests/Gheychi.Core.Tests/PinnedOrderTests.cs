using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class PinnedOrderTests
{
    private static long[] Ids(IEnumerable<long> ids) => ids.ToArray();

    [Fact]
    public void Apply_NoPins_ReturnsTheListAsItIs()
    {
        long[] items = [1, 2, 3];

        Assert.Same(items, PinnedOrder.Apply(items, [], id => id));
    }

    [Fact]
    public void Apply_PutsPinsFirstInPinOrder_AndKeepsTheRest()
    {
        long[] items = [1, 2, 3, 4, 5];

        var ordered = PinnedOrder.Apply(items, [4, 2], id => id);

        Assert.Equal([4L, 2, 1, 3, 5], Ids(ordered));
    }

    [Fact]
    public void Apply_IgnoresPinsWithoutAnItem()
    {
        long[] items = [1, 2, 3];

        var ordered = PinnedOrder.Apply(items, [9, 3, 8], id => id);

        Assert.Equal([3L, 1, 2], Ids(ordered));
    }

    [Fact]
    public void Apply_ADuplicatePin_ListsTheItemOnce()
    {
        long[] items = [1, 2, 3];

        var ordered = PinnedOrder.Apply(items, [2, 2], id => id);

        Assert.Equal([2L, 1, 3], Ids(ordered));
    }

    [Fact]
    public void PinToTop_PutsTheNewOnesAboveTheOldOnes()
    {
        Assert.Equal([5L, 6, 1, 2], PinnedOrder.PinToTop([1, 2], [5, 6]));
    }

    [Fact]
    public void PinToTop_APinnedOneMovesUp_NotTwice()
    {
        Assert.Equal([2L, 1, 3], PinnedOrder.PinToTop([1, 2, 3], [2, 2]));
    }

    [Fact]
    public void Reorder_KeepsPinsThatWereNotOnScreenBelow()
    {
        Assert.Equal([3L, 1, 9], PinnedOrder.Reorder([1, 9, 3], [3, 1]));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(1, 30, 1)]
    [InlineData(1, 40, 2)]
    [InlineData(1, -40, 0)]
    [InlineData(1, -500, 0)]
    [InlineData(1, 500, 3)]
    [InlineData(0, 76, 1)]
    public void TargetIndex_FollowsTheRowsPassed_InsideThePinnedRows(int from, double offset, int expected)
    {
        Assert.Equal(expected, PinnedOrder.TargetIndex(from, offset, 76, 4));
    }

    [Fact]
    public void TargetIndex_NothingPinned_StaysWhereItIs()
    {
        Assert.Equal(2, PinnedOrder.TargetIndex(2, 300, 76, 0));
    }
}
