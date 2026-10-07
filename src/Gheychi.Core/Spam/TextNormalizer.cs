using System.Text;
using System.Text.RegularExpressions;

namespace Gheychi.Core.Spam;

/// <summary>
/// Turns an SMS into the form the spam model is trained on. A copy of Normalizer/TextNormalizer.cs in the
/// sms-spam-dataset repository, which trains the model; TextNormalizerVectors.json pins the behaviour of both copies.
/// </summary>
public static class TextNormalizer
{
    /// <summary>
    /// Written into each model's manifest. Bump it whenever the output changes, so the app does not feed a model
    /// text normalized differently from what it was trained on.
    /// </summary>
    public const int Version = 1;

    public static string Normalize(string? text)
    {
        var modifiedText = text ?? "";

        // 1. Replace HTTP/HTTPS links
        modifiedText = Regex.Replace(modifiedText,
            @"(?:https?://)?(?:[-\w.]|(?:%[\da-fA-F]{2}))+(?:\.[a-zA-Z]{2,})(?:/[^\s]*)?",
            "[LINK_REMOVED]");

        // 2. Replace phone numbers
        modifiedText = Regex.Replace(modifiedText,
            @"(?:\+|00)?(?:\d[\s-]?){9,12}\d",
            "[MASKED_NUMBER]");

        // 3. Remove emojis
        modifiedText = Regex.Replace(modifiedText,
            @"[\uD83C-\uD83E][\uDC00-\uDFFF]|\uD83D[\uDC00-\uDE4F\uDE80-\uDEFF]|[☀-⛿✀-➿]",
            "");

        // 4. Normalize spaces
        modifiedText = Regex.Replace(modifiedText,
            @"[\s‌]+",
            " ").Trim();

        // 5. Convert Latin digits to Persian digits
        modifiedText = ToPersianDigits(modifiedText);

        // 6. Replace dates with "[DATE]"
        modifiedText = Regex.Replace(modifiedText,
            @"[0-9۰-۹]+\/[0-9۰-۹]+\/[0-9۰-۹]+",
            "[DATE]");

        // 7. Replace times with "[TIME]"
        modifiedText = ReplaceTimes(modifiedText);

        // 8. Normalize Persian text
        modifiedText = modifiedText
            .Replace("ك", "ک")
            .Replace("ي", "ی")
            .Replace("ة", "ه")
            .Replace("لغو۱۱", "");

        // 9. Replace USSD codes
        modifiedText = Regex.Replace(modifiedText,
            @"[\*\#][\d\*]{2,15}\#",
            "[USSD_REMOVED]");

        // 10. Replace all sequences of digits with "[RANDOM_NUMBER]"
        modifiedText = Regex.Replace(modifiedText, @"\d+", "[RANDOM_NUMBER]");

        // 11. Temporarily protect placeholders
        modifiedText = modifiedText.Replace("[DATE]", "%%%4%%%");
        modifiedText = modifiedText.Replace("[TIME]", "%%%5%%%");
        modifiedText = modifiedText.Replace("[MASKED_NUMBER]", "%%%6%%%");
        modifiedText = modifiedText.Replace("[NAME]", "%%%7%%%");
        modifiedText = modifiedText.Replace("[ENGLISH]", "%%%8%%%");
        modifiedText = modifiedText.Replace("[LINK_REMOVED]", "%%%1%%%");
        modifiedText = modifiedText.Replace("[RANDOM_NUMBER]", "%%%2%%%");
        modifiedText = modifiedText.Replace("[USSD_REMOVED]", "%%%3%%%");

        // 12. Replace English words with "[ENGLISH]"
        modifiedText = Regex.Replace(modifiedText, @"[A-Za-z]+", "[ENGLISH]");

        // 13. Restore original placeholders
        modifiedText = modifiedText.Replace("%%%4%%%", "[DATE]");
        modifiedText = modifiedText.Replace("%%%5%%%", "[TIME]");
        modifiedText = modifiedText.Replace("%%%6%%%", "[MASKED_NUMBER]");
        modifiedText = modifiedText.Replace("%%%7%%%", "[NAME]");
        modifiedText = modifiedText.Replace("%%%8%%%", "[ENGLISH]");
        modifiedText = modifiedText.Replace("%%%1%%%", "[LINK_REMOVED]");
        modifiedText = modifiedText.Replace("%%%2%%%", "[RANDOM_NUMBER]");
        modifiedText = modifiedText.Replace("%%%3%%%", "[USSD_REMOVED]");

        return modifiedText;
    }

    private static string ReplaceTimes(string text)
    {
        string pattern24 = @"(\b([01]?[0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9])?\b)|" +
                           @"(\b([۰-۹]?[۰-۹]|۱[۰-۹]|۲[۰-۳]):[۰-۵][۰-۹](:[۰-۵][۰-۹])?\b)";

        string pattern12 = @"(\b([1-9]|1[0-2]):[0-5][0-9](:[0-5][0-9])?\s?(AM|PM|am|pm)\b)|" +
                           @"(\b([۱-۹]|۱[۰-۲]):[۰-۵][۰-۹](:[۰-۵][۰-۹])?\s?(ص|ع)\b)";

        string combinedPattern = $@"{pattern24}|{pattern12}";

        return Regex.Replace(text, combinedPattern, "[TIME]");
    }

    private static string ToPersianDigits(string input)
    {
        const string PersianDigits = "۰۱۲۳۴۵۶۷۸۹";

        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c >= '0' && c <= '9')
                sb.Append(PersianDigits[c - '0']);
            else if (c >= '٠' && c <= '٩')
                sb.Append(PersianDigits[c - '٠']);
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
