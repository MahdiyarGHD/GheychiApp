namespace Gheychi.Core.Spam;

/// <summary>Replaces the bundled spam model with a newer one, e.g. a downloaded update.</summary>
public interface ISpamModelUpdater
{
    /// <summary>Version of the model in use; null when none could be loaded.</summary>
    Task<int?> GetActiveVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs a model package (its manifest and model file) and switches to it. Returns false, leaving the
    /// active model in place, when the package does not load or is not newer than the active model.
    /// </summary>
    Task<bool> InstallAsync(Stream manifest, Stream model, CancellationToken cancellationToken = default);
}
