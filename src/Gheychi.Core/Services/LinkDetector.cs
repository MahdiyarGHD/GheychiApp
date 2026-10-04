using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

public static class LinkDetector
{
    private static readonly Regex UrlRegex = new(@"(https?://[^\s]+|www\.[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static (string BodyBefore, string Link) ExtractLink(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, string.Empty);

        var match = UrlRegex.Match(text);
        if (!match.Success)
            return (text, string.Empty);

        var link = match.Value;
        var before = text[..match.Index];
        return (before, link);
    }
}
