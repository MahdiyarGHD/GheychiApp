using System.Globalization;
using System.Text;

namespace Gheychi.Core.Services;

/// <summary>
/// Tells a message that is only a few emoji from one that has words in it: the chat draws the first large, with no
/// bubble. Emoji are counted as the user sees them (a family, a flag, a thumbs up with a skin tone is one), not as
/// code points.
/// </summary>
public static class EmojiText
{
    /// <summary>The most emoji a message can hold and still be drawn large.</summary>
    public const int MaxBig = 3;

    private const int VariationSelector16 = 0xFE0F;
    private const int KeycapMark = 0x20E3;

    /// <summary>
    /// How many emoji the text is made of when it is nothing else (spaces and line breaks aside), from 1 to
    /// <see cref="MaxBig"/>; 0 when it has any other character or more emoji than that.
    /// </summary>
    public static int BigCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64)
            return 0;

        var count = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            if (string.IsNullOrWhiteSpace(element))
                continue;

            if (!IsEmoji(element) || ++count > MaxBig)
                return 0;
        }

        return count;
    }

    // One grapheme cluster: an emoji when it starts with an emoji code point (or is a pair of regional indicators, or a keycap).
    private static bool IsEmoji(string element)
    {
        var first = Rune.GetRuneAt(element, 0);
        var value = first.Value;

        if (IsRegionalIndicator(value))
        {
            // A flag is two letters; one alone is just a letter.
            var next = first.Utf16SequenceLength;
            return next < element.Length && IsRegionalIndicator(Rune.GetRuneAt(element, next).Value);
        }

        if (value is >= '0' and <= '9' or '#' or '*')
            return HasRune(element, KeycapMark);

        if (value >= 0x1F000)
            return IsPictograph(value);

        // The older symbols below U+2BFF are text unless the sequence asks for the emoji look (VS16), except the few
        // that are emoji by default.
        return IsDefaultEmoji(value) || (IsSymbol(value) && HasRune(element, VariationSelector16));
    }

    private static bool HasRune(string element, int value)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            if (rune.Value == value)
                return true;
        }

        return false;
    }

    private static bool IsRegionalIndicator(int value) => value is >= 0x1F1E6 and <= 0x1F1FF;

    // Mahjong tile, joker, the enclosed letters and squares, then the main pictograph blocks up to the extended-A symbols.
    private static bool IsPictograph(int value) =>
        value is 0x1F004 or 0x1F0CF or (>= 0x1F170 and <= 0x1F251) or (>= 0x1F300 and <= 0x1FAFF);

    private static bool IsSymbol(int value) =>
        value is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 ||
        value is (>= 0x2190 and <= 0x21FF) or (>= 0x2300 and <= 0x23FF) or 0x24C2 ||
        value is (>= 0x25A0 and <= 0x27BF) or 0x2934 or 0x2935 or (>= 0x2B05 and <= 0x2B07) or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 ||
        value is 0x3030 or 0x303D or 0x3297 or 0x3299;

    // Symbols that are drawn as emoji without a variation selector.
    private static bool IsDefaultEmoji(int value) =>
        value is 0x231A or 0x231B or (>= 0x23E9 and <= 0x23EC) or 0x23F0 or 0x23F3 or 0x25FD or 0x25FE or 0x2614 or 0x2615 ||
        value is (>= 0x2648 and <= 0x2653) or 0x267F or 0x2693 or 0x26A1 or 0x26AA or 0x26AB or 0x26BD or 0x26BE or 0x26C4 or 0x26C5 ||
        value is 0x26CE or 0x26D4 or 0x26EA or 0x26F2 or 0x26F3 or 0x26F5 or 0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 ||
        value is 0x274C or 0x274E or (>= 0x2753 and <= 0x2755) or 0x2757 or (>= 0x2795 and <= 0x2797) or 0x27B0 or 0x27BF ||
        value is 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55;
}
