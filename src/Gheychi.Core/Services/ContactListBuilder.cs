namespace Gheychi.Core.Services;

public static class ContactListBuilder
{
    public const string OtherLetter = "#";

    /// <summary>Fewest digits of a number a person can be reached on; shorter ones are operator shortcodes.</summary>
    private const int MinPersonalDigits = 7;

    public static string SectionLetter(string? name)
    {
        var first = name?.TrimStart().FirstOrDefault() ?? '\0';
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : OtherLetter;
    }

    /// <summary>
    /// The address to send to when the typed text is a phone number or shortcode ("0912 000-0002",
    /// "+98 912 0000002", "۳۰۰۰"); empty when it is a name or anything else.
    /// </summary>
    public static string TypedAddress(string? text)
    {
        var clean = SearchTextHelper.ToAsciiDigits(text?.Trim() ?? string.Empty);
        if (clean.Length == 0)
            return string.Empty;

        var digits = new System.Text.StringBuilder(clean.Length);
        for (var i = 0; i < clean.Length; i++)
        {
            var c = clean[i];
            if (c is >= '0' and <= '9')
                digits.Append(c);
            else if (c == '+' && i == 0)
                digits.Append(c);
            else if (c is ' ' or '-' or '(' or ')' or '.')
                continue;
            else
                return string.Empty;
        }

        var result = digits.ToString();
        return result.Any(char.IsDigit) ? result : string.Empty;
    }

    /// <summary>True for a normal phone number; false for names ("Bank Mellat") and operator shortcodes ("3000").</summary>
    public static bool IsPersonalNumber(string? address)
    {
        var typed = TypedAddress(address);
        return typed.Count(char.IsDigit) >= MinPersonalDigits;
    }
}
