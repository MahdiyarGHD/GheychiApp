namespace Gheychi.Core.Updates;

/// <summary>
/// Finds a newer version of the app. A stable build is offered stable releases only; a release candidate is offered
/// newer candidates and stable releases too, so testers stay on the channel they installed.
/// </summary>
public sealed class AppUpdates
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly IAppReleaseSource _source;
    private readonly IAppUpdateState _state;
    private readonly AppVersion? _installed;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppUpdates(IAppReleaseSource source, IAppUpdateState state, string installedVersion, Func<DateTime>? utcNow = null)
    {
        _source = source;
        _state = state;
        _installed = AppVersion.Parse(installedVersion);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Raised, possibly off the main thread, when an update becomes available or stops being one.</summary>
    public event EventHandler? Changed;

    /// <summary>The update found by the last check, while it is still newer than the installed version.</summary>
    public AppRelease? GetAvailable()
    {
        var available = _state.Available;
        if (available is null || IsNewer(available))
            return available;

        // The app was updated since.
        SetAvailable(null);
        return null;
    }

    /// <summary>
    /// Asks the source for its releases, unless it was asked within <see cref="CheckInterval"/> and
    /// <paramref name="force"/> is false. Returns the update to offer, if any.
    /// </summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    public async Task<AppRelease?> CheckAsync(bool force, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _utcNow();
            if (!force && _state.LastCheckedUtc is { } last && last <= now && now - last < CheckInterval)
                return GetAvailable();

            var releases = await _source.GetReleasesAsync(cancellationToken);
            AppRelease? found = null;
            foreach (var release in releases.Where(IsNewer).OrderByDescending(r => r.Version))
            {
                if (await _source.HasApkAsync(release, cancellationToken))
                {
                    found = release;
                    break;
                }
            }

            _state.LastCheckedUtc = now;
            SetAvailable(found);
            return _state.Available;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsNewer(AppRelease release)
    {
        // A version that does not read as a release version has nothing to compare with.
        if (_installed is not { } installed || release.Version is not { } version)
            return false;

        if (version.IsCandidate && !installed.IsCandidate)
            return false;

        return version > installed;
    }

    private void SetAvailable(AppRelease? release)
    {
        if (_state.Available == release)
            return;

        _state.Available = release;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
