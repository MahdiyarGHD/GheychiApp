using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// Keeps phone numbers left to right in right-to-left text the app does not draw itself (notifications). A number has
/// no letters to give it a direction, so Android laid "+98 912 000 0003" out in the text's direction and showed its
/// groups backwards ("0003 000 912 98+").
/// </summary>
internal static partial class LtrNumbers
{
    private const char Isolate = '⁦';
    private const char EndIsolate = '⁩';

    // Longer than any number the app shows, so a message body is never run through the pattern.
    private const int MaxLength = 32;

    // A number as the app shows it: digits (Latin or Persian), spaces, dashes, brackets, a leading plus; at least 5 digits.
    [GeneratedRegex(@"^\+?(?:[\s\-()]*[0-9۰-۹٠-٩]){5,}[\s\-()]*$")]
    private static partial Regex PhoneNumber();

    private static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    /// <summary>
    /// Applies to every label whose whole text is a number, in a right-to-left language. Only what is drawn changes:
    /// the bound text, used to send, dial and compare, stays as it is.
    /// </summary>
    public static void Register()
    {
        if (!IsRightToLeft)
            return;

        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((block, e) =>
        {
            if (e.NewValue is string text && NeedsWrap(text))
                block.SetCurrentValue(TextBlock.TextProperty, Wrap(text));
        });
    }

    /// <summary>For a number placed inside other text, such as a notification title or "Send to {0}".</summary>
    public static string Wrap(string text) =>
        NeedsWrap(text) ? $"{Isolate}{text}{EndIsolate}" : text;

    private static bool NeedsWrap(string? text) =>
        IsRightToLeft && text is { Length: > 0 and <= MaxLength } && text[0] != Isolate && PhoneNumber().IsMatch(text);
}
