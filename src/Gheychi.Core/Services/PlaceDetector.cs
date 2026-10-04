using System.Globalization;
using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

/// <summary>
/// Finds places in a message: links to map services, geo: URIs and plain latitude/longitude pairs.
/// </summary>
public static class PlaceDetector
{
    private static readonly Regex GeoUriRegex = new(
        @"\bgeo:(-?\d{1,2}(?:\.\d+)?)\s*,\s*(-?\d{1,3}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Four or more decimals on both numbers: real GPS output ("35.7219, 51.3347"), but not
    // prices, versions or dates.
    private static readonly Regex CoordinateRegex = new(
        @"(?<![\d.])(-?\d{1,2}\.\d{4,})\s*[,،]\s*(-?\d{1,3}\.\d{4,})(?![\d.])",
        RegexOptions.Compiled);

    public static IReadOnlyList<DetectedItem> Find(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return [];

        var places = new List<DetectedItem>();

        foreach (var link in LinkExtractor.Find(body))
        {
            if (IsMapLink(link.OpenUrl))
                places.Add(link);
        }

        // Numbers inside a map URL ("@35.72,51.33,17z") are already covered by the link itself.
        var text = LinkExtractor.StripLinks(body);

        foreach (Match match in GeoUriRegex.Matches(text))
            AddCoordinates(places, match.Groups[1].Value, match.Groups[2].Value);

        foreach (Match match in CoordinateRegex.Matches(text))
            AddCoordinates(places, match.Groups[1].Value, match.Groups[2].Value);

        return places;
    }

    public static bool IsMapLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];

        var path = uri.AbsolutePath.ToLowerInvariant();
        var onMapsPath = path == "/maps" || path.StartsWith("/maps/", StringComparison.Ordinal);

        return host switch
        {
            "maps.app.goo.gl" or "g.page" or "maps.apple.com" or "openstreetmap.org" or "osm.org"
                or "neshan.org" or "nshn.ir" or "balad.ir" or "map.ir" or "mapquest.com"
                or "share.here.com" or "wego.here.com" => true,
            "goo.gl" or "bing.com" => onMapsPath,
            _ when host.StartsWith("maps.google.", StringComparison.Ordinal) => true,
            _ when IsDomainOrSubdomain(host, "waze.com") => true,
            _ when IsDomainOrSubdomain(host, "neshan.org") => true,
            _ when (host.StartsWith("google.", StringComparison.Ordinal) || host.StartsWith("yandex.", StringComparison.Ordinal)) => onMapsPath,
            _ => false
        };
    }

    private static bool IsDomainOrSubdomain(string host, string domain) =>
        host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

    private static void AddCoordinates(List<DetectedItem> places, string latText, string lonText)
    {
        if (!double.TryParse(latText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(lonText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            return;

        if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
            return;

        var title = $"{latText}, {lonText}";
        if (places.Any(p => p.Title == title))
            return;

        places.Add(new DetectedItem(title, string.Empty, $"geo:{latText},{lonText}"));
    }
}
