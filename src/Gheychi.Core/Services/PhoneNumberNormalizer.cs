using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

public static class PhoneNumberNormalizer
{
    private static readonly Regex LetterRegex = new(@"[a-zA-Z\u0600-\u06FF]", RegexOptions.Compiled);
    private static readonly Regex NonDigitRegex = new(@"[^\d]", RegexOptions.Compiled);

    public static bool IsAlphanumeric(string address) =>
        LetterRegex.IsMatch(address);

    public static string ToLookupKey(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        if (IsAlphanumeric(address))
            return address.Trim();

        var digits = NonDigitRegex.Replace(address, string.Empty);
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    public static string FormatDisplay(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || IsAlphanumeric(address))
            return address ?? string.Empty;

        var digits = NonDigitRegex.Replace(address, string.Empty);
        if (digits.StartsWith("98") && digits.Length == 12)
            digits = digits[2..];
        else if (digits.StartsWith("0") && digits.Length == 11)
            digits = digits[1..];

        if (digits.Length == 10 && digits.StartsWith("9"))
            return $"+98 {digits[..3]} {digits.Substring(3, 3)} {digits[6..]}";

        return address;
    }

    /// <summary>
    /// Strips the separators <see cref="FormatDisplay"/> adds ("+98 912 000 0001" -> "+989120000001").
    /// The telephony provider treats an address that still contains spaces as a different
    /// recipient, which files the sent message in a new thread instead of the open one.
    /// </summary>
    public static string ToSendAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || IsAlphanumeric(address))
            return address?.Trim() ?? string.Empty;

        var trimmed = address.Trim();
        var digits = NonDigitRegex.Replace(trimmed, string.Empty);
        return trimmed.StartsWith('+') ? "+" + digits : digits;
    }
}
