namespace Gheychi.Core.Emoji;

/// <summary>
/// The emoji of the picker, read from the data file: one line per category in the order of <see cref="EmojiCategory"/>
/// (without Recent), each line the emoji separated by single spaces. The format is plain on purpose, so reading it is
/// a split and nothing more.
/// </summary>
public static class EmojiCatalog
{
    /// <summary>The emoji of each category, in file order. A line of the file is a category.</summary>
    public static string[][] Parse(string text)
    {
        var lines = text.Split('\n');
        var count = lines.Length;
        while (count > 0 && lines[count - 1].Trim().Length == 0)
            count--;

        var groups = new string[count][];
        for (var i = 0; i < count; i++)
            groups[i] = lines[i].Split([' ', '\r'], StringSplitOptions.RemoveEmptyEntries);

        return groups;
    }
}

/// <summary>The categories of the picker, in the order of the tabs. Recent is not in the data file.</summary>
public enum EmojiCategory
{
    Recent,
    Smileys,
    People,
    Animals,
    Food,
    Travel,
    Activities,
    Objects,
    Symbols,
    Flags
}
