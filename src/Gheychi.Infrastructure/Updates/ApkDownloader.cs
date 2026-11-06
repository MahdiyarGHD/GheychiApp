using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Gheychi.Core.Updates;

namespace Gheychi.Infrastructure.Updates;

/// <summary>
/// Downloads a release's APK into one folder, carrying on where a stopped download left off, and checks it against
/// the SHA-256 the release workflow publishes next to it ("{apk}.sha256", in sha256sum's format). That file comes from
/// the same release, so it only catches a damaged download; the installer checks who signed the APK.
/// </summary>
public sealed class ApkDownloader
{
    private const string PartSuffix = ".part";
    private const int BufferSize = 81920;

    private readonly HttpClient _http;
    private readonly string _directory;

    public ApkDownloader(HttpClient http, string directory)
    {
        _http = http;
        _directory = directory;
    }

    /// <summary>The release's APK if it was downloaded and checked already, otherwise null.</summary>
    public string? GetDownloaded(AppRelease release) => PathFor(release) is { } path && File.Exists(path) ? path : null;

    /// <summary>Downloads the release's APK, checks it and returns its path. Progress goes from 0 to 1.</summary>
    /// <exception cref="InvalidDataException">The download did not match its published SHA-256 and was deleted.</exception>
    /// <exception cref="HttpRequestException">The APK or its SHA-256 could not be downloaded; what came is kept to resume.</exception>
    public async Task<string> DownloadAsync(AppRelease release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var path = PathFor(release) ?? throw new ArgumentException("The release has no APK to download.", nameof(release));
        if (File.Exists(path))
            return path;

        var part = path + PartSuffix;
        Directory.CreateDirectory(_directory);
        // Another release's APK, whole or partial, is of no use any more.
        foreach (var file in Directory.EnumerateFiles(_directory).Where(f => f != part))
            File.Delete(file);

        var url = release.DownloadUrl!;
        var expected = await ReadSha256Async(url + ".sha256", cancellationToken);
        await DownloadToAsync(url, part, progress, cancellationToken);

        byte[] hash;
        await using (var file = File.OpenRead(part))
            hash = await SHA256.HashDataAsync(file, cancellationToken);

        if (!string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidDataException("The downloaded APK does not match its published SHA-256.");
        }

        File.Move(part, path);
        return path;
    }

    /// <summary>Deletes every download.</summary>
    public void Clear()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string? PathFor(AppRelease release) =>
        Uri.TryCreate(release.DownloadUrl, UriKind.Absolute, out var uri)
            ? Path.Combine(_directory, Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath)))
            : null;

    private async Task<string> ReadSha256Async(string url, CancellationToken cancellationToken)
    {
        var text = await _http.GetStringAsync(url, cancellationToken);
        var hex = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (hex is not { Length: 64 } || !hex.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{url} does not hold a SHA-256.");

        return hex;
    }

    private async Task DownloadToAsync(string url, string part, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (have > 0)
            request.Headers.Range = new RangeHeaderValue(have, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        // The part already holds the whole file; the hash tells whether it is the right one.
        if (have > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            return;
        response.EnsureSuccessStatusCode();

        // A server that ignores the range sends the whole file again.
        var resumed = response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
            have = 0;
        var total = response.Content.Headers.ContentLength is { } length ? have + length : 0;

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write,
            FileShare.None, BufferSize, useAsync: true);
        var buffer = new byte[BufferSize];
        var reported = -1;
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            have += read;
            // Whole percents only: a report per buffer would be hundreds of UI updates.
            if (total > 0 && (int)(have * 100 / total) is var percent && percent != reported)
            {
                reported = percent;
                progress?.Report(percent / 100.0);
            }
        }
    }
}
