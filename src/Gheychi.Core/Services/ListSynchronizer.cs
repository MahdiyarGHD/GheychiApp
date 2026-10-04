namespace Gheychi.Core.Services;

public static class ListSynchronizer
{
    /// <summary>
    /// Brings <paramref name="target"/> in line with <paramref name="source"/> using the smallest
    /// set of remove/insert/replace operations, so a bound list only rebinds rows that changed
    /// instead of resetting (which drops the scroll position and rebinds every visible row).
    /// </summary>
    public static void Sync<T, TKey>(
        IList<T> target,
        IReadOnlyList<T> source,
        Func<T, TKey> keyOf,
        Func<T, T, bool> sameContent,
        Action<T, T>? carryOver = null)
        where TKey : notnull
    {
        var wanted = new HashSet<TKey>(source.Count);
        foreach (var item in source)
            wanted.Add(keyOf(item));

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(keyOf(target[i])))
                target.RemoveAt(i);
        }

        for (var i = 0; i < source.Count; i++)
        {
            var incoming = source[i];
            var key = keyOf(incoming);

            if (i < target.Count && EqualityComparer<TKey>.Default.Equals(keyOf(target[i]), key))
            {
                if (!sameContent(target[i], incoming))
                {
                    carryOver?.Invoke(target[i], incoming);
                    target[i] = incoming;
                }
                continue;
            }

            var existingIndex = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (EqualityComparer<TKey>.Default.Equals(keyOf(target[j]), key))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                var existing = target[existingIndex];
                target.RemoveAt(existingIndex);
                if (!sameContent(existing, incoming))
                    carryOver?.Invoke(existing, incoming);
                target.Insert(i, sameContent(existing, incoming) ? existing : incoming);
            }
            else
            {
                target.Insert(i, incoming);
            }
        }
    }

    /// <summary>
    /// True when syncing would need more than <paramref name="limit"/> insert/remove/replace operations.
    /// Each operation is a separate change notification for the bound list, so past a few dozen a
    /// single reset is cheaper than replaying them one by one.
    /// </summary>
    public static bool ExceedsChangeLimit<T, TKey>(
        IList<T> target,
        IReadOnlyList<T> source,
        Func<T, TKey> keyOf,
        Func<T, T, bool> sameContent,
        int limit)
        where TKey : notnull
    {
        var existing = new Dictionary<TKey, T>(target.Count);
        foreach (var item in target)
            existing[keyOf(item)] = item;

        var sourceKeys = new HashSet<TKey>(source.Count);
        var changes = 0;

        foreach (var item in source)
        {
            var key = keyOf(item);
            sourceKeys.Add(key);

            if (!existing.TryGetValue(key, out var current) || !sameContent(current, item))
            {
                if (++changes > limit)
                    return true;
            }
        }

        foreach (var key in existing.Keys)
        {
            if (!sourceKeys.Contains(key) && ++changes > limit)
                return true;
        }

        return false;
    }
}
