using System.Text;

namespace Gheychi.Core.Spam;

/// <summary>Finds newer spam models at the source and installs one when the user asks for it.</summary>
public sealed class SpamModelUpdates
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly ISpamModelSource _source;
    private readonly ISpamModelUpdater _updater;
    private readonly ISpamModelUpdateState _state;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SpamModelUpdates(ISpamModelSource source, ISpamModelUpdater updater, ISpamModelUpdateState state, Func<DateTime>? utcNow = null)
    {
        _source = source;
        _updater = updater;
        _state = state;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Raised, possibly off the main thread, when an update becomes available or stops being one.</summary>
    public event EventHandler? Changed;

    /// <summary>The update found by the last check, while it is still newer than the model in use.</summary>
    public async Task<SpamModelRelease?> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        var available = _state.Available;
        if (available is null || available.Version > await ActiveVersionAsync(cancellationToken))
            return available;

        // Installed since, or an app update bundled it.
        SetAvailable(null);
        return null;
    }

    /// <summary>
    /// Asks the source for the latest model, unless it was asked within <see cref="CheckInterval"/> and
    /// <paramref name="force"/> is false. Returns the update to offer, if any.
    /// </summary>
    /// <exception cref="Exception">The source could not be reached.</exception>
    public async Task<SpamModelRelease?> CheckAsync(bool force, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = _utcNow();
            if (!force && _state.LastCheckedUtc is { } last && last <= now && now - last < CheckInterval)
                return await GetAvailableAsync(cancellationToken);

            var latest = await _source.GetLatestAsync(cancellationToken);
            _state.LastCheckedUtc = now;
            SetAvailable(latest is not null && latest.Version > await ActiveVersionAsync(cancellationToken) ? latest : null);
            return _state.Available;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Downloads and switches to the release. False when the model would not install.</summary>
    /// <exception cref="Exception">The download failed or did not match its manifest.</exception>
    public async Task<bool> InstallAsync(SpamModelRelease release, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            bool installed;
            await using (var model = await _source.DownloadAsync(release, cancellationToken))
            await using (var manifest = new MemoryStream(Encoding.UTF8.GetBytes(release.Manifest)))
                installed = await _updater.InstallAsync(manifest, model, cancellationToken);

            if (installed || release.Version <= await ActiveVersionAsync(cancellationToken))
                SetAvailable(null);
            return installed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> ActiveVersionAsync(CancellationToken cancellationToken) =>
        await _updater.GetActiveVersionAsync(cancellationToken) ?? 0;

    private void SetAvailable(SpamModelRelease? release)
    {
        if (_state.Available == release)
            return;

        _state.Available = release;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
