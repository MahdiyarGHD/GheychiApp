namespace Gheychi.Core.Services;

/// <summary>The emoji used last, newest first, for the picker's first tab.</summary>
public sealed class RecentEmojis(int capacity = 32)
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items => _items;

    public void Add(string emoji)
    {
        _items.Remove(emoji);
        _items.Insert(0, emoji);
        if (_items.Count > capacity)
            _items.RemoveRange(capacity, _items.Count - capacity);
    }

    // Emoji never contain a plain space (joined sequences use U+200D), so a space is a safe separator.
    public string Serialize() => string.Join(' ', _items);

    public static RecentEmojis Parse(string? stored, int capacity = 32)
    {
        var recent = new RecentEmojis(capacity);
        if (string.IsNullOrWhiteSpace(stored))
            return recent;

        // Stored newest first; adding oldest first leaves the same order.
        foreach (var emoji in stored.Split(' ', StringSplitOptions.RemoveEmptyEntries).Reverse())
            recent.Add(emoji);

        return recent;
    }
}
