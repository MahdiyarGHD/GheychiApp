using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

public enum LinkKind
{
    Url,
    Phone
}

/// <summary>A link or a phone number inside a message: where it is in the text, and what it is.</summary>
public readonly record struct LinkSpan(int Start, int Length, LinkKind Kind)
{
    public int End => Start + Length;
}

/// <summary>Finds the links and the phone numbers in a message, so they can be shown highlighted and be tapped.</summary>
public static partial class TextLinker
{
    private const int MinPhoneDigits = 8;
    private const int MinPlainPhoneDigits = 10;
    private const int MaxPhoneDigits = 15;
    private const int MaxDigitsInOneGroup = 5;

    private const string TrailingPunctuation = ".,;:!?'\"»›«‹…،؛؟";

    [GeneratedRegex(@"(?:https?://|www\.)[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeUrlRegex();

    // A short link texted without its scheme ("bit.ly/abc"). Only well-known endings, or every "file.name" would be one.
    [GeneratedRegex(
        @"(?<![\w@./:\-])(?:[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?\.)+(?:com|net|org|ir|io|co|me|ly|gl|gd|to|app|dev|info|biz|xyz|link|online|site|shop|top|tv|ai|edu|gov|us|uk|de|fr|ru|cn|in)(?::\d+)?(?:[/?#][^\s]*)?(?![\w@\-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareUrlRegex();

    [GeneratedRegex(@"^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}$|^\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}$")]
    private static partial Regex DateRegex();

    /// <returns>The links in order of appearance, or null when there are none (nearly every message).</returns>
    public static LinkSpan[]? Find(string? text)
    {
        if (string.IsNullOrEmpty(text) || !MayContainLink(text))
            return null;

        List<LinkSpan>? found = null;

        foreach (Match match in SchemeUrlRegex().Matches(text))
            AddUrl(ref found, text, match);

        if (text.Contains('.'))
        {
            foreach (Match match in BareUrlRegex().Matches(text))
            {
                if (!Overlaps(found, match.Index, match.Length))
                    AddUrl(ref found, text, match);
            }
        }

        FindPhones(ref found, text);

        if (found is null)
            return null;

        found.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return found.ToArray();
    }

    /// <summary>What a tap on the link opens: a web address with its scheme, or a number as digits (with a leading "+" if it had one).</summary>
    public static string Target(string text, LinkSpan span)
    {
        var shown = text.Substring(span.Start, span.Length);
        if (span.Kind == LinkKind.Url)
            return shown.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? shown : "https://" + shown;

        var digits = new char[shown.Length + 1];
        var length = 0;
        if (shown.StartsWith('+'))
            digits[length++] = '+';

        foreach (var c in shown)
        {
            if (char.IsDigit(c))
                digits[length++] = (char)('0' + (int)char.GetNumericValue(c));
        }

        return new string(digits, 0, length);
    }

    private static bool MayContainLink(string text)
    {
        var digits = 0;
        foreach (var c in text)
        {
            if (c == '.' || c == '/')
                return true;

            if (char.IsDigit(c) && ++digits >= MinPhoneDigits)
                return true;
        }

        return false;
    }

    private static void AddUrl(ref List<LinkSpan>? found, string text, Match match)
    {
        var end = match.Index + match.Length;
        while (end > match.Index && IsTrailingNoise(text, match.Index, end))
            end--;

        if (end > match.Index)
            (found ??= []).Add(new LinkSpan(match.Index, end - match.Index, LinkKind.Url));
    }

    private static bool IsTrailingNoise(string text, int start, int end)
    {
        var last = text[end - 1];
        if (TrailingPunctuation.Contains(last))
            return true;

        // A closing bracket belongs to the link when the link has the opening one.
        var (open, close) = last switch { ')' => ('(', ')'), ']' => ('[', ']'), '}' => ('{', '}'), _ => ('\0', '\0') };
        if (open == '\0')
            return false;

        var balance = 0;
        for (var i = start; i < end; i++)
        {
            if (text[i] == open)
                balance++;
            else if (text[i] == close)
                balance--;
        }

        return balance < 0;
    }

    private static bool Overlaps(List<LinkSpan>? found, int start, int length)
    {
        if (found is null)
            return false;

        foreach (var span in found)
        {
            if (start < span.End && span.Start < start + length)
                return true;
        }

        return false;
    }

    private static void FindPhones(ref List<LinkSpan>? found, string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var starts = char.IsDigit(c) || (c == '+' && i + 1 < text.Length && char.IsDigit(text[i + 1]));
            if (!starts || (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '+')))
            {
                i++;
                continue;
            }

            var end = ScanNumber(text, i, out var digits);
            if (IsPhone(text, i, end, digits) && !Overlaps(found, i, end - i))
                (found ??= []).Add(new LinkSpan(i, end - i, LinkKind.Phone));

            // Past the whole number either way, so the tail of a card number is not taken for a phone number.
            i = end;
        }
    }

    /// <summary>The end of the number that starts at <paramref name="start"/>: digits in groups, set apart by spaces, dashes, dots or brackets.</summary>
    private static int ScanNumber(string text, int start, out int digits)
    {
        digits = 0;
        var group = 0;
        var end = start;
        var i = text[start] == '+' ? start + 1 : start;

        while (i < text.Length)
        {
            if (char.IsDigit(text[i]))
            {
                digits++;
                group++;
                end = ++i;
                continue;
            }

            if (!IsSeparator(text[i]))
                break;

            // A long group ends the number: "09121234567 09351234567" is two.
            if (group > MaxDigitsInOneGroup)
                break;

            var next = i + 1;
            if (next < text.Length && IsSeparator(text[next]))
                next++;

            if (next >= text.Length || !char.IsDigit(text[next]))
                break;

            group = 0;
            i = next;
        }

        return end;
    }

    private static bool IsSeparator(char c) => c is ' ' or '-' or '.' or '(' or ')' or ' ';

    private static bool IsPhone(string text, int start, int end, int digits)
    {
        if (end < text.Length && char.IsLetterOrDigit(text[end]))
            return false;

        var plus = text[start] == '+';
        if (!(plus ? digits >= MinPhoneDigits : digits >= MinPlainPhoneDigits) || digits > MaxPhoneDigits)
            return false;

        return !DateRegex().IsMatch(text.AsSpan(start, end - start));
    }
}
