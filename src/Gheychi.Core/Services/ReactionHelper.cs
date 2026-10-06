using System.Text.RegularExpressions;
using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public sealed record ReactionParseResult(bool IsReaction, string? Emoji, string? Snippet);

public static partial class ReactionHelper
{
    private const int MaxSnippetLength = 100;

    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(2);

    private const char CurlyOpen = '“';
    private const char CurlyClose = '”';

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    /// <summary>
    /// Formats an outgoing reaction SMS using Apple's Tapback template so that
    /// iPhone Messages and Google Messages can link it to the original message.
    /// Only the 6 emoji with Tapback verbs are supported (see <see cref="MapEmojiToVerb"/>);
    /// anything else throws, because no other template renders as a reaction on
    /// other SMS apps. The reaction dock offers exactly these 6, so this is unreachable
    /// from the UI and exists to fail fast on programming errors.
    /// Always the English template — even for Persian snippets — because neither
    /// iPhone nor Google Messages recognizes any other template.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="emoji"/> has no Tapback verb.</exception>
    public static string FormatReactionSms(string emoji, string messageBody)
    {
        var verb = MapEmojiToVerb(emoji);
        if (string.IsNullOrEmpty(verb))
            throw new ArgumentException($"Unsupported reaction emoji: '{emoji}'. Only Tapback-verb emoji can be sent.", nameof(emoji));
        // The whole message, never a cut: iPhone links the reaction only when the quote matches the original text,
        // so a reaction to a long message quoting just its start was shown as a plain message there.
        return $"{verb} {CurlyOpen}{CleanQuote(messageBody)}{CurlyClose}";
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

    /// <summary>The start of a message, as older versions of this app quoted it; used to match their reactions.</summary>
    public static string GetSnippet(string body)
    {
        var clean = CleanQuote(body);
        if (clean.Length <= MaxSnippetLength)
            return clean;

        return clean[..MaxSnippetLength].TrimEnd();
    }

    private static string CleanQuote(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        // Preserve newlines: iPhone/Google match the quote verbatim against the
        // original message (observed on-device: "Liked “line1\nline2”"). Collapsing
        // \n to a space breaks that match, so only normalize CRLF -> LF and trim.
        return body.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
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
        s = WhitespaceRuns().Replace(s, " ");
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

    public static SmsMessage? FindReactionTarget(
        IReadOnlyList<SmsMessage> messages,
        SmsMessage reactionMessage,
        string snippet)
    {
        var normalizedSnippet = NormalizeForMatch(snippet);
        if (normalizedSnippet.Length == 0)
            return null;

        SmsMessage? bestExact = null;
        SmsMessage? bestPrefix = null;
        SmsMessage? skewedExact = null;

        foreach (var candidate in messages)
        {
            if (candidate.Id == reactionMessage.Id)
                continue;
            if (candidate.IsOutgoing == reactionMessage.IsOutgoing)
                continue;
            if (candidate.Timestamp > reactionMessage.Timestamp + ClockSkewTolerance)
                continue;
            if (TryParseReaction(candidate.Body).IsReaction)
                continue;

            var normalizedBody = NormalizeForMatch(candidate.Body);
            if (normalizedBody.Length == 0)
                continue;

            // Outgoing rows are stamped with this phone's clock once the send finishes; incoming
            // rows carry the sender's SMSC clock. A reaction answered within seconds can therefore
            // look slightly older than its target, so a small skew is tolerated — but only used
            // when no target at or before the reaction exists.
            if (candidate.Timestamp > reactionMessage.Timestamp)
            {
                if (normalizedBody.Equals(normalizedSnippet, StringComparison.OrdinalIgnoreCase) &&
                    (skewedExact == null || candidate.Timestamp < skewedExact.Timestamp))
                    skewedExact = candidate;
                continue;
            }

            if (normalizedBody.Equals(normalizedSnippet, StringComparison.OrdinalIgnoreCase))
            {
                if (bestExact == null || candidate.Timestamp > bestExact.Timestamp)
                    bestExact = candidate;
            }
            else if (IsPrefixMatch(normalizedBody, normalizedSnippet))
            {
                if (bestPrefix == null || candidate.Timestamp > bestPrefix.Timestamp)
                    bestPrefix = candidate;
            }
        }

        return bestExact ?? bestPrefix ?? skewedExact;
    }

    private static bool IsPrefixMatch(string normalizedBody, string normalizedSnippet)
    {
        if (normalizedBody.StartsWith(normalizedSnippet, StringComparison.OrdinalIgnoreCase))
            return true;

        var truncated = NormalizeForMatch(GetSnippet(normalizedBody));
        return truncated.Length > 0 &&
            normalizedSnippet.StartsWith(truncated, StringComparison.OrdinalIgnoreCase);
    }
}
