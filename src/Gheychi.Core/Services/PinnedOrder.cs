namespace Gheychi.Core.Services;

/// <summary>Puts the pinned conversations at the top of a list, and keeps their order.</summary>
public static class PinnedOrder
{
    /// <summary>
    /// The pinned items first, in the order of <paramref name="pinned"/>, then every other item in the order it came in.
    /// A pin with no item in the list is ignored.
    /// </summary>
    public static IReadOnlyList<T> Apply<T>(IReadOnlyList<T> items, IReadOnlyList<long> pinned, Func<T, long> idOf)
    {
        if (pinned.Count == 0 || items.Count == 0)
            return items;

        var rank = new Dictionary<long, int>(pinned.Count);
        for (var i = 0; i < pinned.Count; i++)
            rank.TryAdd(pinned[i], i);

        var slots = new T?[pinned.Count];
        var taken = new bool[pinned.Count];
        var rest = new List<T>(items.Count);
        foreach (var item in items)
        {
            if (rank.TryGetValue(idOf(item), out var slot) && !taken[slot])
            {
                slots[slot] = item;
                taken[slot] = true;
            }
            else
            {
                rest.Add(item);
            }
        }

        var ordered = new List<T>(items.Count);
        for (var i = 0; i < slots.Length; i++)
        {
            if (taken[i])
                ordered.Add(slots[i]!);
        }

        ordered.AddRange(rest);
        return ordered;
    }

    /// <summary>The pins with <paramref name="added"/> above them; one that was pinned already moves up.</summary>
    public static List<long> PinToTop(IReadOnlyList<long> current, IEnumerable<long> added)
    {
        var result = new List<long>(current.Count);
        var seen = new HashSet<long>();
        foreach (var id in added)
        {
            if (seen.Add(id))
                result.Add(id);
        }

        foreach (var id in current)
        {
            if (seen.Add(id))
                result.Add(id);
        }

        return result;
    }

    /// <summary>The order the user chose for the pins on screen, followed by the stored pins that were not on screen.</summary>
    public static List<long> Reorder(IReadOnlyList<long> stored, IReadOnlyList<long> visibleOrder)
    {
        var result = new List<long>(stored.Count);
        var seen = new HashSet<long>();
        foreach (var id in visibleOrder)
        {
            if (seen.Add(id))
                result.Add(id);
        }

        foreach (var id in stored)
        {
            if (seen.Add(id))
                result.Add(id);
        }

        return result;
    }

    /// <summary>The slot a dragged row falls into: how many rows it has passed, kept inside the pinned rows.</summary>
    public static int TargetIndex(int from, double offset, double rowHeight, int pinnedCount)
    {
        if (pinnedCount <= 0 || rowHeight <= 0)
            return from;

        var passed = (int)Math.Round(offset / rowHeight, MidpointRounding.AwayFromZero);
        return Math.Clamp(from + passed, 0, pinnedCount - 1);
    }
}
