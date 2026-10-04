using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

/// <summary>A link or place found inside a message body.</summary>
/// <param name="Title">What the row shows: the link as written, or the coordinates.</param>
/// <param name="Host">Domain without "www."; empty for coordinates.</param>
/// <param name="OpenUrl">Something the OS can open (scheme always present).</param>
public sealed record DetectedItem(string Title, string Host, string OpenUrl);

public static class LinkExtractor
{
    private static readonly Regex UrlRegex = new(
        @"(?:https?://|www\.)[^\s<>""“”«»]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly char[] TrailingPunctuation =
        ['.', ',', ';', ':', '!', '?', '\'', '"', '>', ']', '}', '»', '”', '’', '،', '؛', '؟', '…'];

    public static IReadOnlyList<DetectedItem> Find(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return [];

        List<DetectedItem>? found = null;
        foreach (Match match in UrlRegex.Matches(body))
        {
            var url = Clean(match.Value);
            var host = HostOf(url);
            if (host.Length == 0)
                continue;

            found ??= [];
            if (found.Any(f => string.Equals(f.Title, url, StringComparison.OrdinalIgnoreCase)))
                continue;

            found.Add(new DetectedItem(url, host, ToOpenUrl(url)));
        }

        return found ?? (IReadOnlyList<DetectedItem>)[];
    }

    /// <summary>Removes the text of every URL so other detectors do not re-read numbers inside them.</summary>
    public static string StripLinks(string body) => UrlRegex.Replace(body, " ");

    public static string HostOf(string url)
    {
        if (!Uri.TryCreate(ToOpenUrl(url), UriKind.Absolute, out var uri))
            return string.Empty;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        return host.Contains('.') ? host : string.Empty;
    }

    public static string ToOpenUrl(string url) =>
        url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;

    private static string Clean(string url)
    {
        var trimmed = url.TrimEnd(TrailingPunctuation);

        // "(see https://x.com/a)" - the closing bracket belongs to the sentence, not the URL.
        if (trimmed.EndsWith(')') && !trimmed.Contains('('))
            trimmed = trimmed.TrimEnd(')').TrimEnd(TrailingPunctuation);

        return trimmed;
    }
}
