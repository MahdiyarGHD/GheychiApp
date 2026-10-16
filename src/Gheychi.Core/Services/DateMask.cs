using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

/// <summary>
/// Blanks out the dates of a message (and the time after them), so that no phone number or code is read from their
/// digits. The text keeps its length: what was a date is a run of letters, and every index stays where it was.
/// </summary>
internal static partial class DateMask
{
    // Three groups of digits with one separator: whether it is a date is decided by the numbers (see IsDate). The time
    // that follows ("1405.07.29 00:15", or just the hour) belongs to it, or it would be joined onto the digits of a phone number.
    [GeneratedRegex(
        @"(?<![\d/.\-])(?<a>\d{1,4})(?<s>[/.\-])(?<b>\d{1,2})\k<s>(?<c>\d{1,4})(?:[ T]+\d{1,2}(?::\d{2}){0,2}(?!\d))?(?!\d|[/.\-]\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    public static string Apply(string text)
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
}
