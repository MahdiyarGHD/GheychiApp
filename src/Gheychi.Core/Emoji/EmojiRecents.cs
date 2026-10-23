namespace Gheychi.Core.Emoji;

/// <summary>The emoji picked most lately, newest first, kept in a single stored string.</summary>
public static class EmojiRecents
{
    public const int MaxCount = 32;

    /// <summary>The list with <paramref name="emoji"/> in front: moved there when already in it, and the oldest dropped past <see cref="MaxCount"/>.</summary>
    public static string[] Push(IReadOnlyList<string> recent, string emoji)
    {
        var list = new List<string>(Math.Min(recent.Count + 1, MaxCount)) { emoji };
        foreach (var item in recent)
        {
            if (list.Count >= MaxCount)
                break;
            if (item != emoji)
                list.Add(item);
        }

        return [.. list];
    }

    public static string Serialize(IEnumerable<string> recent) => string.Join(' ', recent);

    /// <summary>The stored list, tolerating blanks, repeats and a list longer than <see cref="MaxCount"/>.</summary>
    public static string[] Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var item in stored.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (list.Count >= MaxCount)
                break;
            if (seen.Add(item))
                list.Add(item);
        }

        return [.. list];
    }
}
