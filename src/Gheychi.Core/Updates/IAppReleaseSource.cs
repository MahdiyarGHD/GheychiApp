namespace Gheychi.Core.Updates;

/// <summary>Where the app's releases are published.</summary>
public interface IAppReleaseSource
{
    /// <summary>The published releases, stable and candidates, in no particular order.</summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    Task<IReadOnlyList<AppRelease>> GetReleasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The release with <see cref="AppRelease.DownloadUrl"/> set to the APK this device should download, or null
    /// while it has none yet. A release is listed as soon as its tag is pushed, while the release workflow is still
    /// building the APKs it attaches.
    /// </summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    Task<AppRelease?> FindApkAsync(AppRelease release, CancellationToken cancellationToken = default);
}
