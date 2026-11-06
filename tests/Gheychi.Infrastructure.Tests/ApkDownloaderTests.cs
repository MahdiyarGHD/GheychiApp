using System.Net;
using System.Security.Cryptography;
using System.Text;
using Gheychi.Core.Updates;
using Gheychi.Infrastructure.Updates;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class ApkDownloaderTests : IDisposable
{
    private const string ApkUrl = "https://example.test/download/v1.2.0/Gheychi-1.2.0-arm64-v8a.apk";

    private static readonly AppRelease Release = new("v1.2.0", "https://example.test/tag/v1.2.0", ApkUrl);
    private static readonly byte[] Apk = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("0123456789", 50)));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gheychi-apk-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Download_MatchingHash_KeepsTheApk()
    {
        var downloader = new ApkDownloader(new HttpClient(new ApkServer(Apk)), _directory);

        var path = await downloader.DownloadAsync(Release);

        Assert.Equal(Apk, await File.ReadAllBytesAsync(path));
        Assert.Equal("Gheychi-1.2.0-arm64-v8a.apk", Path.GetFileName(path));
        Assert.Equal(path, downloader.GetDownloaded(Release));
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Download_ReportsProgressUpToWhole()
    {
        var reports = new List<double>();
        var downloader = new ApkDownloader(new HttpClient(new ApkServer(Apk)), _directory);

        await downloader.DownloadAsync(Release, new SyncProgress(reports.Add));

        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task Download_HashMismatch_DeletesAndThrows()
    {
        var server = new ApkServer(Apk) { Sha256 = new string('0', 64) };
        var downloader = new ApkDownloader(new HttpClient(server), _directory);

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(Release));

        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Null(downloader.GetDownloaded(Release));
    }

    [Fact]
    public async Task Download_Sha256FileUnreadable_Throws()
    {
        var server = new ApkServer(Apk) { Sha256 = "<html>Not Found</html>" };
        var downloader = new ApkDownloader(new HttpClient(server), _directory);

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(Release));
    }

    [Fact]
    public async Task Download_AfterAStop_AsksForTheRestOnly()
    {
        Directory.CreateDirectory(_directory);
        var part = Path.Combine(_directory, "Gheychi-1.2.0-arm64-v8a.apk.part");
        await File.WriteAllBytesAsync(part, Apk[..200]);
        var server = new ApkServer(Apk);

        var path = await new ApkDownloader(new HttpClient(server), _directory).DownloadAsync(Release);

        Assert.Equal(200, server.RangeFrom);
        Assert.Equal(Apk, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public async Task Download_ServerIgnoresTheRange_StartsOver()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "Gheychi-1.2.0-arm64-v8a.apk.part"), new byte[300]);
        var server = new ApkServer(Apk) { HonourRange = false };

        var path = await new ApkDownloader(new HttpClient(server), _directory).DownloadAsync(Release);

        Assert.Equal(Apk, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Download_WholePartAlreadyThere_ChecksItWithoutDownloading()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "Gheychi-1.2.0-arm64-v8a.apk.part"), Apk);
        var server = new ApkServer(Apk);

        var path = await new ApkDownloader(new HttpClient(server), _directory).DownloadAsync(Release);

        Assert.Equal(Apk, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Download_DeletesAnotherReleasesFiles()
    {
        Directory.CreateDirectory(_directory);
        var old = Path.Combine(_directory, "Gheychi-1.1.0-arm64-v8a.apk");
        await File.WriteAllBytesAsync(old, [1, 2, 3]);

        await new ApkDownloader(new HttpClient(new ApkServer(Apk)), _directory).DownloadAsync(Release);

        Assert.False(File.Exists(old));
    }

    [Fact]
    public async Task Download_Interrupted_KeepsWhatCame()
    {
        var server = new ApkServer(Apk) { FailAfter = 100 };
        var downloader = new ApkDownloader(new HttpClient(server), _directory);

        await Assert.ThrowsAnyAsync<IOException>(() => downloader.DownloadAsync(Release));

        Assert.Equal(100, new FileInfo(Path.Combine(_directory, "Gheychi-1.2.0-arm64-v8a.apk.part")).Length);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class ApkServer(byte[] apk) : HttpMessageHandler
    {
        public string Sha256 { get; init; } = Convert.ToHexString(SHA256.HashData(apk)).ToLowerInvariant() + "  Gheychi-1.2.0-arm64-v8a.apk\n";
        public bool HonourRange { get; init; } = true;
        public int? FailAfter { get; init; }
        public long? RangeFrom { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url == ApkUrl + ".sha256")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sha256) });
            if (url != ApkUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var from = request.Headers.Range?.Ranges.Single().From;
            RangeFrom = from;
            if (from is { } start && HonourRange)
            {
                if (start >= apk.Length)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));

                var rest = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(apk[(int)start..]) };
                rest.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, apk.Length - 1, apk.Length);
                return Task.FromResult(rest);
            }

            HttpContent content = FailAfter is { } count
                ? new StreamContent(new FailingStream(apk[..count]))
                : new ByteArrayContent(apk);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>A body that breaks off after its bytes, as a dropped connection does.</summary>
    private sealed class FailingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position < Length ? base.ReadAsync(buffer, cancellationToken) : throw new IOException("Connection reset.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Position < Length ? base.ReadAsync(buffer, offset, count, cancellationToken) : throw new IOException("Connection reset.");
    }
}
