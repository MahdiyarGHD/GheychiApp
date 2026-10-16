using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

public enum LinkKind
{
    Url,
    Phone,
    /// <summary>A USSD code ("*815*3453#"): a tap opens the dialer with it filled in.</summary>
    Ussd
}

/// <summary>A link, a phone number or a USSD code inside a message: where it is in the text, and what it is.</summary>
public readonly record struct LinkSpan(int Start, int Length, LinkKind Kind)
{
    public int End => Start + Length;
}

/// <summary>Finds the links, phone numbers and USSD codes in a message, so they can be shown highlighted and be tapped.</summary>
public static partial class TextLinker
{
    private const int MinPhoneDigits = 8;
    private const int MinPlainPhoneDigits = 10;
    private const int MaxPhoneDigits = 15;
    private const int MaxDigitsInOneGroup = 5;
    private const int MaxUssdLength = 32;

    private const string TrailingPunctuation = ".,;:!?'\"»›«‹…،؛؟";

    [GeneratedRegex(@"(?:https?://|www\.)[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeUrlRegex();

    // A short link texted without its scheme ("bit.ly/abc"). Only well-known endings, or every "file.name" would be one.
    [GeneratedRegex(
        @"(?<![\w@./:\-])(?:[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?\.)+(?:com|net|org|ir|io|co|me|ly|gl|gd|to|app|dev|info|biz|xyz|link|online|site|shop|top|tv|ai|edu|gov|us|uk|de|fr|ru|cn|in)(?::\d+)?(?:[/?#][^\s]*)?(?![\w@\-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareUrlRegex();

    // Three groups of digits with one separator: whether it is a date is decided by the numbers (see IsDate). The time
    // that follows ("1405.07.29 00:15", or just the hour) belongs to it, or it would be joined onto the digits of a phone number.
    [GeneratedRegex(
        @"(?<![\d/.\-])(?<a>\d{1,4})(?<s>[/.\-])(?<b>\d{1,2})\k<s>(?<c>\d{1,4})(?:[ T]+\d{1,2}(?::\d{2}){0,2}(?!\d))?(?!\d|[/.\-]\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    // "*100#", "*815*3453#", "*#06#": stars, digits and hashes, set apart from the words around it, ending in a hash.
    [GeneratedRegex(@"(?<![\w*#])(?=[*#]*\d)[*#][\d*#]{1,31}#(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex UssdRegex();

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

        FindUssd(ref found, text);
        FindPhones(ref found, MaskDates(text));

        if (found is null)
            return null;

        found.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return found.ToArray();
    }

    /// <summary>
    /// What a tap on the link opens: a web address with its scheme, a number as digits (with a leading "+" if it had
    /// one), or a USSD code with its stars and hashes and Latin digits.
    /// </summary>
    public static string Target(string text, LinkSpan span)
    {
        var shown = text.Substring(span.Start, span.Length);
        if (span.Kind == LinkKind.Url)
            return shown.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? shown : "https://" + shown;

        var target = new char[shown.Length];
        var length = 0;
        foreach (var c in shown)
        {
            if (char.IsDigit(c))
                target[length++] = (char)('0' + (int)char.GetNumericValue(c));
            else if (c == '*' || c == '#' || (c == '+' && length == 0))
                target[length++] = c;
        }

        return new string(target, 0, length);
    }

    private static bool MayContainLink(string text)
    {
        var digits = 0;
        foreach (var c in text)
        {
            if (c == '.' || c == '/' || c == '#')
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

    private static void FindUssd(ref List<LinkSpan>? found, string text)
    {
        if (!text.Contains('#'))
            return;

        foreach (Match match in UssdRegex().Matches(text))
        {
            if (match.Length <= MaxUssdLength && !Overlaps(found, match.Index, match.Length))
                (found ??= []).Add(new LinkSpan(match.Index, match.Length, LinkKind.Ussd));
        }
    }

    /// <summary>The text with its dates (and the time after them) blanked out by letters, so that no phone number is read from their digits.</summary>
    private static string MaskDates(string text)
    {
        if (!text.Contains('.') && !text.Contains('/') && !text.Contains('-'))
            return text;

        return DateRegex().Replace(text, static match =>
            IsDate(match.Groups["a"].Value, match.Groups["b"].Value, match.Groups["c"].Value)
                ? new string('x', match.Length)
                : match.Value);
    }

    // year-month-day (Gregorian or Jalali, "1405.07.29"), or day-month-year and the two-digit years of both.
    private static bool IsDate(string a, string b, string c)
    {
        var (first, second, third) = (Number(a), Number(b), Number(c));
        if (second is < 1 or > 12)
            return false;

        if (a.Length == 4)
            return c.Length <= 2 && third is >= 1 and <= 31;

        if (c.Length == 4)
            return a.Length <= 2 && first is >= 1 and <= 31;

        return a.Length <= 2 && c.Length <= 2 && ((first is >= 1 and <= 31) || (third is >= 1 and <= 31));
    }

    private static int Number(string digits)
    {
        var value = 0;
        foreach (var c in digits)
            value = value * 10 + (int)char.GetNumericValue(c);

        return value;
    }

    private static bool IsSeparator(char c) => c is ' ' or '-' or '.' or '(' or ')' or ' ';

    private static bool IsPhone(string text, int start, int end, int digits)
    {
        if (end < text.Length && char.IsLetterOrDigit(text[end]))
            return false;

        var plus = text[start] == '+';
        return (plus ? digits >= MinPhoneDigits : digits >= MinPlainPhoneDigits) && digits <= MaxPhoneDigits;
    }
}
