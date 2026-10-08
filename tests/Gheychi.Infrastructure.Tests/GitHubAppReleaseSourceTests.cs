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

    private const string UniversalApk = "https://github.com/MahdiyarGHD/GheychiApp/releases/download/v1.2.0/Gheychi-1.2.0.apk";
    private const string Arm64Apk = "https://github.com/MahdiyarGHD/GheychiApp/releases/download/v1.2.0/Gheychi-1.2.0-arm64-v8a.apk";

    private static readonly AppRelease Release = new("v1.2.0", "https://example.test/tag/v1.2.0", UniversalApk);

    [Fact]
    public void ApkUrl_WithAbi_NamesTheSingleAbiApk()
    {
        Assert.Equal(UniversalApk, GitHubAppReleaseSource.ApkUrl("v1.2.0"));
        Assert.Equal(Arm64Apk, GitHubAppReleaseSource.ApkUrl("v1.2.0", "arm64-v8a"));
    }

    [Theory]
    [InlineData(new[] { "arm64-v8a", "armeabi-v7a", "armeabi" }, "arm64-v8a")]
    [InlineData(new[] { "armeabi-v7a", "armeabi" }, "armeabi-v7a")]
    [InlineData(new[] { "x86_64", "x86", "arm64-v8a" }, "x86_64")]
    [InlineData(new[] { "armeabi" }, null)]
    [InlineData(new string[0], null)]
    public void ChooseAbi_TakesTheMostPreferredReleasedAbi(string[] supported, string? expected) =>
        Assert.Equal(expected, GitHubAppReleaseSource.ChooseAbi(supported));

    [Theory]
    [InlineData(HttpStatusCode.Found, true)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task FindApk_NoAbi_ReadsTheUniversalApkStatus(HttpStatusCode status, bool found)
    {
        var handler = new StatusHandler(_ => status);
        var source = new GitHubAppReleaseSource(new HttpClient(handler));

        Assert.Equal(found ? Release : null, await source.FindApkAsync(Release));
        Assert.Equal([UniversalApk], handler.Requested);
        Assert.Equal(HttpMethod.Head, handler.Method);
    }

    [Fact]
    public async Task FindApk_WithAbi_OffersItsApk()
    {
        var source = new GitHubAppReleaseSource(new HttpClient(new StatusHandler(_ => HttpStatusCode.Found)), "arm64-v8a");

        Assert.Equal(Arm64Apk, (await source.FindApkAsync(Release))?.DownloadUrl);
    }

    [Fact]
    public async Task FindApk_WithAbi_NotAttached_OffersTheUniversalApk()
    {
        var handler = new StatusHandler(url => url == Arm64Apk ? HttpStatusCode.NotFound : HttpStatusCode.Found);
        var source = new GitHubAppReleaseSource(new HttpClient(handler), "arm64-v8a");

        Assert.Equal(UniversalApk, (await source.FindApkAsync(Release))?.DownloadUrl);
        Assert.Equal([Arm64Apk, UniversalApk], handler.Requested);
    }

    [Fact]
    public async Task FindApk_WithAbi_NoneAttached_ReturnsNull()
    {
        var source = new GitHubAppReleaseSource(new HttpClient(new StatusHandler(_ => HttpStatusCode.NotFound)), "arm64-v8a");

        Assert.Null(await source.FindApkAsync(Release));
    }

    [Fact]
    public async Task FindApk_ServerError_Throws()
    {
        var source = new GitHubAppReleaseSource(new HttpClient(new StatusHandler(_ => HttpStatusCode.BadGateway)), "arm64-v8a");

        await Assert.ThrowsAsync<HttpRequestException>(() => source.FindApkAsync(Release));
    }

    private sealed class StatusHandler(Func<string, HttpStatusCode> status) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            var url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            return Task.FromResult(new HttpResponseMessage(status(url)));
        }
    }
}
