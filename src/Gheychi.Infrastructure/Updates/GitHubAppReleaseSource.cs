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

    public async Task<bool> HasApkAsync(AppRelease release, CancellationToken cancellationToken = default)
    {
        if (release.DownloadUrl is null)
            return false;

        // Only the headers: GitHub answers a missing asset with 404 and a published one with a redirect to the file.
        using var request = new HttpRequestMessage(HttpMethod.Head, release.DownloadUrl);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        if ((int)response.StatusCode >= 400)
            throw new HttpRequestException($"Checking {release.DownloadUrl} returned {(int)response.StatusCode}.");

        return true;
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
                releases.Add(new AppRelease(tag, uri.AbsoluteUri, ApkUrl(tag)));
        }

        return releases;
    }

    /// <summary>
    /// The APK the release workflow attaches: "Gheychi-{version}.apk", the version being the tag without a leading "v".
    /// A direct link, so the browser downloads it instead of showing the release page.
    /// </summary>
    public static string ApkUrl(string tag)
    {
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        return $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/Gheychi-{Uri.EscapeDataString(version)}.apk";
    }
}
