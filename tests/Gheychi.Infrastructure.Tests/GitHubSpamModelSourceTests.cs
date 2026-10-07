using System.Net;
using System.Security.Cryptography;
using System.Text;
using Gheychi.Infrastructure.Spam;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class GitHubSpamModelSourceTests : IDisposable
{
    private static readonly byte[] ModelBytes = [10, 20, 30, 40, 50];

    private readonly string _downloads = Path.Combine(Path.GetTempPath(), $"gheychi_downloads_{Guid.NewGuid():N}");
    private readonly FakeHandler _handler = new();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_downloads))
                Directory.Delete(_downloads, recursive: true);
        }
        catch
        {
        }
    }

    private GitHubSpamModelSource Create() => new(new HttpClient(_handler), _downloads);

    private static string Manifest(int version = 5, long? size = null, string? sha256 = null, int normalizer = 1) =>
        $$"""
        {"version":{{version}},"model":"spam.mlnet","textColumn":"Text","labelColumn":"Label","spamLabel":"spam",
         "normalizer":{{normalizer}},"size":{{size ?? ModelBytes.Length}},"sha256":"{{sha256 ?? Convert.ToHexStringLower(SHA256.HashData(ModelBytes))}}"}
        """;

    private string[] Leftovers() => Directory.Exists(_downloads) ? Directory.GetFiles(_downloads) : [];

    [Fact]
    public async Task GetLatest_ReadsTheLatestReleaseManifest()
    {
        _handler.Respond("releases/latest/download/manifest.json", Manifest());

        var release = await Create().GetLatestAsync();

        Assert.NotNull(release);
        Assert.Equal((5, (long)ModelBytes.Length), (release.Version, release.Size));
        Assert.Equal(Manifest(), release.Manifest);
    }

    [Fact]
    public async Task GetLatest_NoRelease_IsNull()
    {
        Assert.Null(await Create().GetLatestAsync());
    }

    [Fact]
    public async Task GetLatest_NormalizerThisAppLacks_IsNull()
    {
        _handler.Respond("releases/latest/download/manifest.json", Manifest(normalizer: Core.Spam.TextNormalizer.Version + 1));

        Assert.Null(await Create().GetLatestAsync());
    }

    [Fact]
    public async Task GetLatest_ServerError_Throws()
    {
        _handler.Status = HttpStatusCode.ServiceUnavailable;

        await Assert.ThrowsAsync<HttpRequestException>(() => Create().GetLatestAsync());
    }

    [Fact]
    public async Task Download_FetchesTheTaggedModel_AndDeletesItWhenDisposed()
    {
        _handler.Respond("releases/download/model-v5/spam.mlnet", ModelBytes);
        var release = new Core.Spam.SpamModelRelease(5, ModelBytes.Length, Manifest());

        await using (var stream = await Create().DownloadAsync(release))
        {
            var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal(ModelBytes, copy.ToArray());
        }

        Assert.Empty(Leftovers());
    }

    [Theory]
    [InlineData(4, null)]
    [InlineData(null, "00")]
    public async Task Download_NotMatchingManifest_ThrowsAndLeavesNothing(int? size, string? sha256)
    {
        _handler.Respond("releases/download/model-v5/spam.mlnet", ModelBytes);
        var release = new Core.Spam.SpamModelRelease(5, ModelBytes.Length, Manifest(size: size, sha256: sha256));

        await Assert.ThrowsAsync<InvalidDataException>(() => Create().DownloadAsync(release));
        Assert.Empty(Leftovers());
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _responses = new();

        public HttpStatusCode? Status { get; set; }

        public void Respond(string pathEnd, string body) => _responses[pathEnd] = Encoding.UTF8.GetBytes(body);

        public void Respond(string pathEnd, byte[] body) => _responses[pathEnd] = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Assert.StartsWith($"https://github.com/{GitHubSpamModelSource.Repository}/", url);

            if (Status is { } status)
                return Task.FromResult(new HttpResponseMessage(status));

            var match = _responses.FirstOrDefault(r => url.EndsWith(r.Key, StringComparison.Ordinal));
            return Task.FromResult(match.Value is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(match.Value) });
        }
    }
}
