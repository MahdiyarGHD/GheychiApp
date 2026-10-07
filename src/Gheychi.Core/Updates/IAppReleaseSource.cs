namespace Gheychi.Core.Updates;

/// <summary>Where the app's releases are published.</summary>
public interface IAppReleaseSource
{
    /// <summary>The published releases, stable and candidates, in no particular order.</summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    Task<IReadOnlyList<AppRelease>> GetReleasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the release's APK can be downloaded yet. A release is listed as soon as its tag is pushed, while the
    /// release workflow is still building the APK it attaches.
    /// </summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    Task<bool> HasApkAsync(AppRelease release, CancellationToken cancellationToken = default);
}
