using System.Globalization;

namespace Gheychi.Core.Emoji;

/// <summary>Typing an emoji or a backspace into a text at the caret, the way a keyboard would.</summary>
public static class EmojiTextEdit
{
    /// <summary>Replaces the selection (or inserts at the caret) with <paramref name="insert"/>; the caret ends after it.</summary>
    public static (string Text, int Caret) Insert(string? text, int selectionStart, int selectionEnd, string insert)
    {
        text ??= string.Empty;
        var (start, end) = Clamp(text, selectionStart, selectionEnd);
        return (string.Concat(text.AsSpan(0, start), insert, text.AsSpan(end)), start + insert.Length);
    }

    /// <summary>
    /// Deletes the selection, or when there is none the letter before the caret: a whole emoji, however many code
    /// points it is made of (a family, a flag, a face with a skin tone). Nothing changes at the start of the text.
    /// </summary>
    public static (string Text, int Caret) Backspace(string? text, int selectionStart, int selectionEnd)
    {
        text ??= string.Empty;
        var (start, end) = Clamp(text, selectionStart, selectionEnd);
        if (start == end)
        {
            if (start == 0)
                return (text, 0);

            start = PreviousBoundary(text, start);
        }

        return (string.Concat(text.AsSpan(0, start), text.AsSpan(end)), start);
    }

    private static (int Start, int End) Clamp(string text, int a, int b) =>
        (Math.Clamp(Math.Min(a, b), 0, text.Length), Math.Clamp(Math.Max(a, b), 0, text.Length));

    private static int PreviousBoundary(string text, int caret)
    {
        var last = 0;
        foreach (var index in StringInfo.ParseCombiningCharacters(text))
        {
            if (index >= caret)
                break;
            last = index;
        }

        return last;
    }
}
