using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

public sealed record ReactionParseResult(bool IsReaction, string? Emoji, string? Snippet);

public static class ReactionHelper
{
    private const int MaxSnippetLength = 100;

    private const char CurlyOpen = '“';
    private const char CurlyClose = '”';

    private static readonly Regex WhitespaceRuns = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Formats an outgoing reaction SMS using Apple's Tapback fallback template so that
    /// iPhone Messages and Google Messages can link it to the original message.
    /// Known emojis map to verbs (Liked/Loved/Laughed at/...); anything else falls back
    /// to <c>Reacted {emoji} to “snippet”</c> with curly quotes, exactly as observed
    /// on-device (<c>Reacted 🙏 to “سلام صبح بخیر.… ”</c>).
    /// Always the English template — even for Persian snippets — because neither
    /// iPhone nor Google Messages recognizes any other template.
    /// </summary>
    public static string FormatReactionSms(string emoji, string messageBody)
    {
        var snippet = GetSnippet(messageBody);
        return FormatWithVerb(MapEmojiToVerb(emoji), emoji, snippet);
    }

    private static string FormatWithVerb(string? verb, string emoji, string snippet)
    {
        if (!string.IsNullOrEmpty(verb))
            return $"{verb} {CurlyOpen}{snippet}{CurlyClose}";
        return $"Reacted {emoji} to {CurlyOpen}{snippet}{CurlyClose}";
    }

    /// <summary>Maps a reaction emoji to its Apple Tapback verb, or null for the generic fallback.</summary>
    public static string? MapEmojiToVerb(string emoji)
    {
        var normalized = (emoji ?? string.Empty).Replace("\uFE0F", string.Empty);
        return normalized switch
        {
            "👍" => "Liked",
            "❤️" or "❤" => "Loved",
            "😂" => "Laughed at",
            "👎" => "Disliked",
            "‼️" or "‼" or "❗" or "❕" => "Emphasized",
            "❓" or "❔" => "Questioned",
            _ => null
        };
    }

    public static string GetSnippet(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        // Preserve newlines: iPhone/Google match the snippet verbatim against the
        // original message (observed on-device: "Liked “line1\nline2”"). Collapsing
        // \n to a space breaks that match, so only normalize CRLF -> LF and trim.
        var clean = body.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
        if (clean.Length <= MaxSnippetLength)
            return clean;

        return clean[..MaxSnippetLength].TrimEnd();
    }

    /// <summary>
    /// Normalizes two texts for fuzzy snippet matching: unifies quote styles and
    /// collapses all whitespace runs (including newlines) to a single space so that
    /// old space-collapsed snippets still match newline-preserving bodies and vice versa.
    /// </summary>
    public static string NormalizeForMatch(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var s = text.Trim()
            .Replace('“', '"').Replace('”', '"')
            .Replace('«', '"').Replace('»', '"')
            .Replace("\r\n", "\n").Replace('\r', '\n');
        s = WhitespaceRuns.Replace(s, " ");
        return s;
    }

    public static ReactionParseResult TryParseReaction(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new ReactionParseResult(false, null, null);

        var text = body.Trim();

        if (text.StartsWith("واکنش ", StringComparison.Ordinal))
        {
            var afterPrefix = text["واکنش ".Length..].TrimStart();
            var beIndex = afterPrefix.IndexOf(" به ", StringComparison.Ordinal);
            if (beIndex > 0)
            {
                var emoji = afterPrefix[..beIndex].Trim();
                var remainder = afterPrefix[(beIndex + " به ".Length)..].Trim();
                var snippet = ExtractQuotedSnippet(remainder);
                if (!string.IsNullOrEmpty(emoji) && !string.IsNullOrEmpty(snippet))
                    return new ReactionParseResult(true, emoji, snippet);
            }
        }

        if (text.StartsWith("Reacted ", StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = text["Reacted ".Length..].TrimStart();
            var toIndex = afterPrefix.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
            if (toIndex > 0)
            {
                var emoji = afterPrefix[..toIndex].Trim();
                var remainder = afterPrefix[(toIndex + " to ".Length)..].Trim();
                var snippet = ExtractQuotedSnippet(remainder);
                if (!string.IsNullOrEmpty(emoji) && !string.IsNullOrEmpty(snippet))
                    return new ReactionParseResult(true, emoji, snippet);
            }
        }

        if (text.StartsWith("Liked ", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = ExtractQuotedSnippet(text["Liked ".Length..].Trim());
            if (!string.IsNullOrEmpty(snippet))
                return new ReactionParseResult(true, "👍", snippet);
        }
        if (text.StartsWith("Loved ", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = ExtractQuotedSnippet(text["Loved ".Length..].Trim());
            if (!string.IsNullOrEmpty(snippet))
                return new ReactionParseResult(true, "❤️", snippet);
        }
        if (text.StartsWith("Laughed at ", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = ExtractQuotedSnippet(text["Laughed at ".Length..].Trim());
            if (!string.IsNullOrEmpty(snippet))
                return new ReactionParseResult(true, "😂", snippet);
        }
        if (text.StartsWith("Disliked ", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = ExtractQuotedSnippet(text["Disliked ".Length..].Trim());
            if (!string.IsNullOrEmpty(snippet))
                return new ReactionParseResult(true, "👎", snippet);
        }
        if (text.StartsWith("Emphasized ", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = ExtractQuotedSnippet(text["Emphasized ".Length..].Trim());
            if (!string.IsNullOrEmpty(snippet))
                return new ReactionParseResult(true, "‼️", snippet);
        }
        if (text.StartsWith("Questioned ", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = ExtractQuotedSnippet(text["Questioned ".Length..].Trim());
            if (!string.IsNullOrEmpty(snippet))
                return new ReactionParseResult(true, "❓", snippet);
        }

        return new ReactionParseResult(false, null, null);
    }

    private static string? ExtractQuotedSnippet(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if ((text.StartsWith('"') && text.EndsWith('"')) ||
            (text.StartsWith('“') && text.EndsWith('”')) ||
            (text.StartsWith('«') && text.EndsWith('»')))
        {
            return text[1..^1].Trim();
        }

        var startIdx = text.IndexOfAny(['"', '“', '«']);
        if (startIdx >= 0)
        {
            var endChar = text[startIdx] switch
            {
                '“' => '”',
                '«' => '»',
                _ => '"'
            };
            var endIdx = text.LastIndexOf(endChar);
            if (endIdx > startIdx)
                return text[(startIdx + 1)..endIdx].Trim();
        }

        return null;
    }
}
