namespace Gheychi.Core.Services;

public static class SearchTextHelper
{
    /// <summary>
    /// Returns the query plus Persian/Arabic spelling and digit variants, so "علی" typed with an
    /// Arabic keyboard still finds text stored with Persian letters (and the other way round).
    /// </summary>
    public static IReadOnlyList<string> BuildVariants(string? text)
    {
        var clean = text?.Trim();
        if (string.IsNullOrEmpty(clean))
            return [];

        var variants = new List<string> { clean };
        AddIfNew(variants, MapChars(clean, toPersian: true));
        AddIfNew(variants, MapChars(clean, toPersian: false));
        AddIfNew(variants, ToAsciiDigits(clean));
        return variants;
    }

    public static bool ContainsAny(string? haystack, IReadOnlyList<string> needles)
    {
        if (string.IsNullOrEmpty(haystack))
            return false;

        foreach (var needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void AddIfNew(List<string> list, string value)
    {
        if (value.Length > 0 && !list.Contains(value, StringComparer.Ordinal))
            list.Add(value);
    }

    private static string ToAsciiDigits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '۰' and <= '۹')
                chars[i] = (char)('0' + (c - '۰'));
            else if (c is >= '٠' and <= '٩')
                chars[i] = (char)('0' + (c - '٠'));
        }
        return new string(chars);
    }

    private static string MapChars(string text, bool toPersian)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            switch (c)
            {
                case 'ي' or 'ى' when toPersian: chars[i] = 'ی'; break;
                case 'ی' when !toPersian: chars[i] = 'ي'; break;
                case 'ك' when toPersian: chars[i] = 'ک'; break;
                case 'ک' when !toPersian: chars[i] = 'ك'; break;
                case >= '٠' and <= '٩' when toPersian: chars[i] = (char)('۰' + (c - '٠')); break;
                case >= '۰' and <= '۹' when !toPersian: chars[i] = (char)('٠' + (c - '۰')); break;
                case >= '0' and <= '9' when toPersian: chars[i] = (char)('۰' + (c - '0')); break;
            }
        }
        return new string(chars);
    }
}
