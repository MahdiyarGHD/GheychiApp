namespace Gheychi.Core.Services;

/// <summary>
/// Senders the user blocked in Gheychi. Their messages are dropped as they arrive: never stored, shown or notified.
/// Calls are not blocked; that takes the phone's own block list.
/// </summary>
public interface IBlockedSenders
{
    /// <summary>Raised after a sender was blocked or unblocked.</summary>
    event EventHandler? Changed;

    bool IsBlocked(string address);

    void SetBlocked(string address, bool blocked);

    /// <summary>The blocked senders' lookup keys.</summary>
    IReadOnlyList<string> GetAll();
}
