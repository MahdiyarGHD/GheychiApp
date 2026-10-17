namespace Gheychi.Core.Services;

/// <summary>The conversations pinned to the top of the inbox, in the order the user keeps them.</summary>
public interface IPinStore
{
    /// <summary>Counts every change, so a list built from the pins can tell that it is out of date.</summary>
    int Version { get; }

    /// <summary>The pinned conversations, the one at the top first.</summary>
    IReadOnlyList<long> GetOrder();

    /// <summary>Pins the conversations above the ones already pinned, in the order given.</summary>
    void PinToTop(IEnumerable<long> threadIds);

    void Unpin(IEnumerable<long> threadIds);

    /// <summary>Keeps the order the user dragged the visible pins into; pins that are not on screen stay below them.</summary>
    void SetOrder(IReadOnlyList<long> visibleOrder);
}
