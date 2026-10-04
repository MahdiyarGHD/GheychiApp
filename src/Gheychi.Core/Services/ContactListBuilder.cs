using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public sealed record ContactSection(string Letter, IReadOnlyList<ContactEntry> Contacts);

public static class ContactListBuilder
{
    public const string OtherLetter = "#";

    public static string SectionLetter(string? name)
    {
        var first = name?.TrimStart().FirstOrDefault() ?? '\0';
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : OtherLetter;
    }

    /// <summary>Contacts matching <paramref name="query"/> (name, or number digits), grouped by first letter.</summary>
    public static IReadOnlyList<ContactSection> Build(IEnumerable<ContactEntry> contacts, string? query)
    {
        var needles = SearchTextHelper.BuildVariants(query);
        var queryDigits = DigitsOf(SearchTextHelper.ToAsciiDigits(query ?? string.Empty));

        IEnumerable<ContactEntry> matches = contacts;
        if (needles.Count > 0)
        {
            matches = contacts.Where(c =>
                SearchTextHelper.ContainsAny(c.Name, needles) ||
                SearchTextHelper.ContainsAny(c.Number, needles) ||
                (queryDigits.Length > 0 && DigitsOf(c.Number).Contains(queryDigits, StringComparison.Ordinal)));
        }

        return matches
            .GroupBy(c => SectionLetter(c.Name))
            .OrderBy(g => g.Key == OtherLetter ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ContactSection(
                g.Key,
                g.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                 .ThenBy(c => c.Number, StringComparer.Ordinal)
                 .ToList()))
            .ToList();
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

    private static string DigitsOf(string text) => new(text.Where(c => c is >= '0' and <= '9').ToArray());
}
