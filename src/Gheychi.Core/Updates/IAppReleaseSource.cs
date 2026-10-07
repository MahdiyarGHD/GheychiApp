namespace Gheychi.Core.Updates;

/// <summary>Where the app's releases are published.</summary>
public interface IAppReleaseSource
{
    /// <summary>The published releases, stable and candidates, in no particular order.</summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    Task<IReadOnlyList<AppRelease>> GetReleasesAsync(CancellationToken cancellationToken = default);
}
