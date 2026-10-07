using System.Globalization;
using System.Text.RegularExpressions;
using Android.Widget;
using Microsoft.Maui.Handlers;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// Keeps phone numbers left to right in a right-to-left layout. A number has no letters to give it a direction, so
/// Android laid "+98 912 000 0003" out in the page's direction and showed its groups backwards ("0003 000 912 98+").
/// Only what is drawn is changed: the bound text, used to send, dial and compare, stays as it is.
/// </summary>
internal static partial class LtrNumbers
{
    private const char Isolate = '⁦';
    private const char EndIsolate = '⁩';

    // A number as the app shows it: digits (Latin or Persian), spaces, dashes, brackets, a leading plus; at least 5 digits.
    [GeneratedRegex(@"^\+?(?:[\s\-()]*[0-9۰-۹٠-٩]){5,}[\s\-()]*$")]
    private static partial Regex PhoneNumber();

    private static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    /// <summary>Applies to every label whose whole text is a number.</summary>
    public static void Register() =>
        LabelHandler.Mapper.AppendToMapping(nameof(ILabel.Text), (handler, label) =>
        {
            if (IsRightToLeft && label is Label { FormattedText: null, Text: { Length: > 0 } text }
                && handler.PlatformView is TextView view && PhoneNumber().IsMatch(text))
                view.Text = Wrap(text);
        });

    /// <summary>For a number placed inside other text, such as a notification title or "Send to {0}".</summary>
    public static string Wrap(string text) =>
        IsRightToLeft && text.Length > 0 && text[0] != Isolate && PhoneNumber().IsMatch(text)
            ? $"{Isolate}{text}{EndIsolate}"
            : text;
}
