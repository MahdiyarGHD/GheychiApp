namespace Gheychi.Core.Spam;

public interface ISpamClassifier
{
    /// <summary>Null when no model could be loaded; the message is then treated as not spam.</summary>
    Task<SpamScore?> ScoreAsync(string text, CancellationToken cancellationToken = default);
}
