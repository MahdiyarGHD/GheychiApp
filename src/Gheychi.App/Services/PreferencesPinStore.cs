using Gheychi.Core.Services;

namespace Gheychi.App.Services;

/// <summary>
/// The pinned conversations, kept in the app preferences. Held in memory and written through: the inbox reads the order
/// on every refresh.
/// </summary>
public sealed class PreferencesPinStore : IPinStore
{
    private const string Key = "pinned_threads_v1";

    private readonly object _gate = new();
    private List<long> _order = [];
    private int _version;

    public PreferencesPinStore()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, string.Empty);
            _order = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => long.TryParse(s, out var id) ? id : -1)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the pins failed: {ex}");
        }
    }

    public int Version
    {
        get
        {
            lock (_gate)
                return _version;
        }
    }

    public IReadOnlyList<long> GetOrder()
    {
        lock (_gate)
            return _order.ToList();
    }

    public void PinToTop(IEnumerable<long> threadIds)
    {
        lock (_gate)
            Replace(PinnedOrder.PinToTop(_order, threadIds.Where(id => id > 0)));
    }

    public void Unpin(IEnumerable<long> threadIds)
    {
        lock (_gate)
        {
            var gone = threadIds.ToHashSet();
            if (_order.Exists(gone.Contains))
                Replace(_order.Where(id => !gone.Contains(id)).ToList());
        }
    }

    public void SetOrder(IReadOnlyList<long> visibleOrder)
    {
        lock (_gate)
            Replace(PinnedOrder.Reorder(_order, visibleOrder));
    }

    private void Replace(List<long> order)
    {
        if (order.SequenceEqual(_order))
            return;

        _order = order;
        _version++;
        try
        {
            Preferences.Default.Set(Key, string.Join(",", _order));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving the pins failed: {ex}");
        }
    }
}
