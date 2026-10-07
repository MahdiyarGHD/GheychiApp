using System.Xml.Linq;
using Gheychi.Core.Updates;

namespace Gheychi.Infrastructure.Updates;

/// <summary>The app's GitHub releases, published by the release workflow.</summary>
public sealed class GitHubAppReleaseSource : IAppReleaseSource
{
    public const string Repository = "MahdiyarGHD/GheychiApp";

    // The releases feed rather than the GitHub API, which limits unauthenticated clients to 60 requests an hour per IP.
    // It lists candidates as well; which is which is read from the tag.
    public const string FeedUrl = $"https://github.com/{Repository}/releases.atom";

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private readonly HttpClient _http;

    public GitHubAppReleaseSource(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<AppRelease>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(FeedUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var feed = await response.Content.ReadAsStreamAsync(cancellationToken);
        return Parse(await XDocument.LoadAsync(feed, LoadOptions.None, cancellationToken));
    }

    // The feed holds only the newest releases (10), which is all a check needs.
    public static IReadOnlyList<AppRelease> Parse(XDocument feed)
    {
        var releases = new List<AppRelease>();
        foreach (var entry in feed.Root?.Elements(Atom + "entry") ?? [])
        {
            var page = entry.Elements(Atom + "link")
                .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")
                ?.Attribute("href")?.Value;
            if (!Uri.TryCreate(page, UriKind.Absolute, out var uri))
                continue;

            var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
            if (segments.Length < 2 || segments[^2] != "tag")
                continue;

            var tag = Uri.UnescapeDataString(segments[^1]);
            if (AppVersion.Parse(tag) is not null)
                releases.Add(new AppRelease(tag, uri.AbsoluteUri));
        }

        return releases;
    }
}
