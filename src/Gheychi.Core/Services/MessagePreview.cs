using System.Text;

namespace Gheychi.Core.Services;

/// <summary>
/// The one-line preview of a message in a list. A label cut to one line would show the first line of the text only;
/// here the line breaks become spaces, so the whole message runs on and the label's ellipsis falls where the row ends.
/// </summary>
public static class MessagePreview
{
    // Far more than a row can show: a long message is not laid out in full only to be cut.
    public const int MaxLength = 200;

    public static string SingleLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var span = text.AsSpan().Trim();
        var cut = span.Length > MaxLength;
        if (cut)
            span = span[..(char.IsHighSurrogate(span[MaxLength - 1]) ? MaxLength - 1 : MaxLength)];

        if (span.IndexOfAny("\r\n\u0085\u2028\u2029") < 0)
            return cut ? string.Concat(span, "…") : span.Length == text.Length ? text : span.ToString();

        // The spaces around a line break go with it: "a \n\n b" reads "a b".
        var builder = new StringBuilder(span.Length + 1);
        var lineBreak = false;
        foreach (var c in span)
        {
            if (c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')
            {
                while (builder.Length > 0 && char.IsWhiteSpace(builder[^1]))
                    builder.Length--;
                lineBreak = true;
                continue;
            }

            if (lineBreak)
            {
                if (char.IsWhiteSpace(c))
                    continue;

                builder.Append(' ');
                lineBreak = false;
            }

            builder.Append(c);
        }

        if (cut)
            builder.Append('…');
        return builder.ToString();
    }
}
