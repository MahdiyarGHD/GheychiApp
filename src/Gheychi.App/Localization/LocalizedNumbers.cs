using System.Globalization;
using Gheychi.Core.Services;

namespace Gheychi.App.Localization;

/// <summary>Digits in the script of the language in use.</summary>
public static class LocalizedNumbers
{
    public static bool IsPersian => CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    public static string Digits(string text) => IsPersian ? DateFormattingService.ToPersianDigits(text) : text;

    public static string Number(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));
}
