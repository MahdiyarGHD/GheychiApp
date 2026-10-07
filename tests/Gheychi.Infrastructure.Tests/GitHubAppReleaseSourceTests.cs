using System.Net;
using System.Xml.Linq;
using Gheychi.Core.Updates;
using Gheychi.Infrastructure.Updates;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class GitHubAppReleaseSourceTests
{
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/" xml:lang="en-US">
          <link type="text/html" rel="alternate" href="https://github.com/MahdiyarGHD/GheychiApp/releases"/>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/MahdiyarGHD/GheychiApp/releases/tag/1.3.0-rc.1"/>
            <title>1.3.0-rc.1</title>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/MahdiyarGHD/GheychiApp/releases/tag/v1.2.0"/>
            <title>v1.2.0</title>
          </entry>
          <entry>
            <link rel="alternate" type="text/html" href="https://github.com/MahdiyarGHD/GheychiApp/releases/tag/nightly"/>
            <title>nightly</title>
          </entry>
        </feed>
        """;

    [Fact]
    public void Parse_ReadsReleaseTagsAndPages_SkipsOtherTags()
    {
        var releases = GitHubAppReleaseSource.Parse(XDocument.Parse(Feed));

        Assert.Equal(
            [
                new AppRelease("1.3.0-rc.1", "https://github.com/MahdiyarGHD/GheychiApp/releases/tag/1.3.0-rc.1",
                    "https://github.com/MahdiyarGHD/GheychiApp/releases/download/1.3.0-rc.1/Gheychi-1.3.0-rc.1.apk"),
                new AppRelease("v1.2.0", "https://github.com/MahdiyarGHD/GheychiApp/releases/tag/v1.2.0",
                    "https://github.com/MahdiyarGHD/GheychiApp/releases/download/v1.2.0/Gheychi-1.2.0.apk")
            ],
            releases);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task HasApk_ReadsTheDownloadStatus(HttpStatusCode status, bool expected)
    {
        var handler = new StatusHandler(status);
        var source = new GitHubAppReleaseSource(new HttpClient(handler));
        var release = new AppRelease("v1.2.0", "https://example.test/tag/v1.2.0", "https://example.test/Gheychi-1.2.0.apk");

        Assert.Equal(expected, await source.HasApkAsync(release));
        Assert.Equal(HttpMethod.Head, handler.Method);
    }

    [Fact]
    public async Task HasApk_ServerError_Throws()
    {
        var source = new GitHubAppReleaseSource(new HttpClient(new StatusHandler(HttpStatusCode.BadGateway)));
        var release = new AppRelease("v1.2.0", "https://example.test/tag/v1.2.0", "https://example.test/Gheychi-1.2.0.apk");

        await Assert.ThrowsAsync<HttpRequestException>(() => source.HasApkAsync(release));
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
