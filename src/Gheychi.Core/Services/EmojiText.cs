using System.Globalization;

namespace Gheychi.Core.Services;

/// <summary>Editing the message draft from the emoji picker, where the text box itself is not being typed in.</summary>
public static class EmojiText
{
    /// <summary>Puts <paramref name="emoji"/> at the cursor; returns the new text and where the cursor goes.</summary>
    public static (string Text, int Cursor) Insert(string? text, int cursor, string emoji)
    {
        var current = text ?? string.Empty;
        var at = Math.Clamp(cursor, 0, current.Length);
        return (current.Insert(at, emoji), at + emoji.Length);
    }

    /// <summary>
    /// Deletes the character before the cursor as the user sees it: a whole emoji (including joined sequences such as
    /// a family), not half of its surrogate pair.
    /// </summary>
    public static (string Text, int Cursor) DeleteBefore(string? text, int cursor)
    {
        var current = text ?? string.Empty;
        var at = Math.Clamp(cursor, 0, current.Length);
        if (at == 0)
            return (current, 0);

        var starts = StringInfo.ParseCombiningCharacters(current[..at]);
        var start = starts[^1];
        return (current.Remove(start, at - start), start);
    }
}
