namespace Gheychi.App.Gestures;

/// <summary>A screen that reacts to a horizontal swipe anywhere on it.</summary>
public interface IPageSwipeClient
{
    /// <summary>-1 accepts leftward swipes, +1 rightward, 0 none right now.</summary>
    int AllowedSwipeSign { get; }

    void OnSwipeStarted();

    /// <summary>Signed horizontal travel of the finger in dp since the swipe was recognised.</summary>
    void OnSwipeMoved(double offsetDp);

    void OnSwipeEnded(double offsetDp, double velocityDpPerSecond, bool cancelled);
}

/// <summary>
/// The activity sees every touch before the views do; it hands horizontal swipes to the active
/// client and cancels the touch for the list underneath so both never react to the same drag.
/// </summary>
public static class PageSwipe
{
    public static IPageSwipeClient? Client { get; set; }
}
