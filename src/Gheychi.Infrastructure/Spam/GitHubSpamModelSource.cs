using System.Net;
using System.Security.Cryptography;
using System.Text;
using Gheychi.Core.Spam;

namespace Gheychi.Infrastructure.Spam;

/// <summary>Models published as GitHub releases of the dataset repository, by its train workflow.</summary>
public sealed class GitHubSpamModelSource : ISpamModelSource
{
    public const string Repository = "MahdiyarGHD/sms-spam-dataset";

    // Release download links rather than the GitHub API, which limits unauthenticated clients to 60 requests an hour per IP.
    private const string LatestManifestUrl = $"https://github.com/{Repository}/releases/latest/download/{SpamModelManifest.FileName}";

    private readonly HttpClient _http;
    private readonly string _downloadDirectory;

    public GitHubSpamModelSource(HttpClient http, string downloadDirectory)
    {
        _http = http;
        _downloadDirectory = downloadDirectory;
    }

    public async Task<SpamModelRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(LatestManifestUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var manifest = SpamModelManifest.Parse(Encoding.UTF8.GetBytes(json));
            return manifest is { Size: { } size, Sha256: not null } ? new SpamModelRelease(manifest.Version, size, json) : null;
        }
        catch (InvalidDataException ex)
        {
            System.Diagnostics.Debug.WriteLine($"The published spam model is not usable by this app: {ex.Message}");
            return null;
        }
    }

    public async Task<Stream> DownloadAsync(SpamModelRelease release, CancellationToken cancellationToken = default)
    {
        var manifest = SpamModelManifest.Parse(Encoding.UTF8.GetBytes(release.Manifest));
        Directory.CreateDirectory(_downloadDirectory);
        var path = Path.Combine(_downloadDirectory, $"spam-model-{manifest.Version}-{Guid.NewGuid():N}.download");
        try
        {
            // By tag rather than "latest": a release published since the check would pair this manifest with another model.
            var url = $"https://github.com/{Repository}/releases/download/model-v{manifest.Version}/{manifest.ModelFile}";
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = File.Create(path);
                await body.CopyToAsync(file, cancellationToken);
            }

            byte[] hash;
            await using (var file = File.OpenRead(path))
            {
                if (file.Length != manifest.Size)
                    throw new InvalidDataException($"The downloaded spam model is {file.Length} bytes; its manifest says {manifest.Size}.");
                hash = await SHA256.HashDataAsync(file, cancellationToken);
            }

            if (!string.Equals(Convert.ToHexString(hash), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded spam model does not match its manifest's SHA-256.");

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }
}
