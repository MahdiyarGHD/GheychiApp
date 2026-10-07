using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Android.Text;
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

    // Longer than any number the app shows, so a message body is never run through the pattern.
    private const int MaxLength = 32;

    private static readonly ConditionalWeakTable<TextView, Watcher> Watched = new();

    // A number as the app shows it: digits (Latin or Persian), spaces, dashes, brackets, a leading plus; at least 5 digits.
    [GeneratedRegex(@"^\+?(?:[\s\-()]*[0-9۰-۹٠-٩]){5,}[\s\-()]*$")]
    private static partial Regex PhoneNumber();

    private static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    /// <summary>
    /// Applies to every label whose whole text is a number. The native text is watched rather than the Text mapping
    /// alone: other label mappings (text transform, text type) write the text to the view again after it, which
    /// undid the wrapping and left the numbers reversed.
    /// </summary>
    public static void Register() =>
        LabelHandler.Mapper.AppendToMapping(nameof(ILabel.Text), (handler, label) =>
        {
            if (!IsRightToLeft || label is not Label control || handler.PlatformView is not TextView view)
                return;

            Watched.GetValue(view, v =>
            {
                var watcher = new Watcher(control);
                v.AddTextChangedListener(watcher);
                return watcher;
            });
            Apply(view, control);
        });

    /// <summary>For a number placed inside other text, such as a notification title or "Send to {0}".</summary>
    public static string Wrap(string text) =>
        NeedsWrap(text) ? $"{Isolate}{text}{EndIsolate}" : text;

    private static bool NeedsWrap(string? text) =>
        IsRightToLeft && text is { Length: > 0 and <= MaxLength } && text[0] != Isolate && PhoneNumber().IsMatch(text);

    private static void Apply(TextView view, Label label)
    {
        // Formatted and HTML text carry spans that rewriting the plain text would drop.
        if (label.FormattedText is not null || label.TextType != TextType.Text)
            return;

        var text = view.Text;
        if (NeedsWrap(text))
            view.Text = Wrap(text!);
    }

    private sealed class Watcher(Label label) : Java.Lang.Object, ITextWatcher
    {
        private readonly WeakReference<Label> _label = new(label);
        private bool _applying;

        public void AfterTextChanged(IEditable? s)
        {
            // Setting the text from here fires this again; the flag stops the second round.
            if (_applying || s is null || !NeedsWrap(s.ToString()) || !_label.TryGetTarget(out var label)
                || label.Handler?.PlatformView is not TextView view)
                return;

            _applying = true;
            try
            {
                Apply(view, label);
            }
            finally
            {
                _applying = false;
            }
        }

        public void BeforeTextChanged(Java.Lang.ICharSequence? s, int start, int count, int after)
        {
        }

        public void OnTextChanged(Java.Lang.ICharSequence? s, int start, int before, int count)
        {
        }
    }
}
