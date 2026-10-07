namespace Gheychi.Core.Spam;

/// <summary>Where newer spam models are published.</summary>
public interface ISpamModelSource
{
    /// <summary>The latest published model; null when none is published or this app cannot use it.</summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    Task<SpamModelRelease?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>The release's model file, already checked against its manifest. Disposing the stream discards the download.</summary>
    /// <exception cref="InvalidDataException">The download does not match the manifest.</exception>
    Task<Stream> DownloadAsync(SpamModelRelease release, CancellationToken cancellationToken = default);
}
