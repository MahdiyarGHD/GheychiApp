using Gheychi.Core.Spam;

namespace Gheychi.Infrastructure.Spam;

public sealed class MlNetSpamClassifier : ISpamClassifier, ISpamModelUpdater
{
    private readonly SpamModelStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SpamModel? _model;
    private bool _loadAttempted;
    private volatile bool _loadFailed;

    public MlNetSpamClassifier(SpamModelStore store)
    {
        _store = store;
    }

    public async Task<SpamScore?> ScoreAsync(string text, CancellationToken cancellationToken = default)
    {
        var model = await GetModelAsync(cancellationToken);
        if (model is null)
            return null;

        try
        {
            return new SpamScore(model.Score(text), model.Version);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Spam scoring failed: {ex}");
            return null;
        }
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default) => GetModelAsync(cancellationToken);

    // Read without loading the model when it is not loaded yet: a load takes seconds, and the Settings tab asks for the
    // version as soon as it appears.
    public async Task<int?> GetActiveVersionAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _model) is { } model)
            return model.Version;
        if (_loadFailed)
            return null;

        return await Task.Run(() => _store.PeekActiveVersionAsync(cancellationToken));
    }

    public async Task<bool> InstallAsync(Stream manifest, Stream model, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var active = await LoadOnceAsync(cancellationToken);
            var installed = await Task.Run(() => _store.InstallAsync(manifest, model, active?.Version ?? 0, cancellationToken));
            if (installed is null)
                return false;

            // A score already running on the old model finishes on it; the old model is dropped, not disposed.
            Volatile.Write(ref _model, installed);
            await Task.Run(() => _store.DeleteOlderThan(installed.Version));
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SpamModel?> GetModelAsync(CancellationToken cancellationToken)
    {
        var model = Volatile.Read(ref _model);
        if (model is not null)
            return model;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadOnceAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Loading is slow; after a failure every message would pay for it again, so it is retried only in a new process.
    private async Task<SpamModel?> LoadOnceAsync(CancellationToken cancellationToken)
    {
        if (_model is null && !_loadAttempted)
        {
            _loadAttempted = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Never on the caller's thread, which can be the UI: the store's awaits would bring the load back to it.
                Volatile.Write(ref _model, await Task.Run(() => _store.LoadActiveAsync(cancellationToken)));
            }
            finally
            {
                _loadFailed = _model is null;
            }

            System.Diagnostics.Debug.WriteLine($"Spam model {_model?.Version.ToString() ?? "none"} loaded in {watch.ElapsedMilliseconds} ms");
        }

        return _model;
    }
}
