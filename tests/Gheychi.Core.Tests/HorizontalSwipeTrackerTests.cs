using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class HorizontalSwipeTrackerTests
{
    private static HorizontalSwipeTracker Started()
    {
        var tracker = new HorizontalSwipeTracker(slop: 10);
        tracker.Begin(500, 500);
        return tracker;
    }

    [Fact]
    public void Undecided_BelowSlop()
    {
        var tracker = Started();

        Assert.False(tracker.Move(495, 502, allowedSign: -1));
        Assert.False(tracker.IsDragging);
        Assert.False(tracker.IsRejected);
    }

    [Fact]
    public void HorizontalMove_InAllowedDirection_StartsDragAtZeroOffset()
    {
        var tracker = Started();

        Assert.True(tracker.Move(488, 503, allowedSign: -1));
        Assert.True(tracker.IsDragging);
        Assert.Equal(-2, tracker.Offset, 6);

        Assert.True(tracker.Move(400, 510, allowedSign: -1));
        Assert.Equal(-90, tracker.Offset, 6);
    }

    [Fact]
    public void VerticalMove_IsRejected_AndStaysRejected()
    {
        var tracker = Started();

        Assert.False(tracker.Move(505, 480, allowedSign: -1));
        Assert.True(tracker.IsRejected);

        Assert.False(tracker.Move(300, 480, allowedSign: -1));
        Assert.False(tracker.IsDragging);
    }

    [Fact]
    public void DiagonalMove_IsRejected()
    {
        var tracker = Started();

        Assert.False(tracker.Move(485, 485, allowedSign: -1));
        Assert.True(tracker.IsRejected);
    }

    [Fact]
    public void WrongDirection_IsRejected()
    {
        var tracker = Started();

        Assert.False(tracker.Move(530, 500, allowedSign: -1));
        Assert.True(tracker.IsRejected);
    }

    [Fact]
    public void NoAllowedDirection_IsRejected()
    {
        var tracker = Started();

        Assert.False(tracker.Move(450, 500, allowedSign: 0));
        Assert.True(tracker.IsRejected);
    }

    [Fact]
    public void Drag_FollowsFingerBackPastOrigin()
    {
        var tracker = Started();
        tracker.Move(450, 500, allowedSign: -1);

        Assert.True(tracker.Move(520, 500, allowedSign: -1));
        Assert.Equal(30, tracker.Offset, 6);
    }

    [Fact]
    public void Begin_ClearsPreviousGesture()
    {
        var tracker = Started();
        tracker.Move(505, 480, allowedSign: -1);
        Assert.True(tracker.IsRejected);

        tracker.Begin(500, 500);

        Assert.False(tracker.IsRejected);
        Assert.True(tracker.Move(450, 500, allowedSign: -1));
    }

    [Fact]
    public void Move_WithoutBegin_IsIgnored()
    {
        var tracker = new HorizontalSwipeTracker(10);

        Assert.False(tracker.Move(100, 0, allowedSign: -1));
        Assert.False(tracker.IsDragging);
    }
}
