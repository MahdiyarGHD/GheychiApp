namespace Gheychi.Core.Spam;

public sealed class SpamDetector(ISpamClassifier classifier, ISpamSettings settings)
{
    public const float DefaultThreshold = 0.85f;

    /// <summary>The score when the message is spam; null when it is not, or when it could not be classified.</summary>
    public async Task<SpamScore?> DetectAsync(string body, bool fromContact, CancellationToken cancellationToken = default)
    {
        if (fromContact || string.IsNullOrWhiteSpace(body))
            return null;

        var score = await classifier.ScoreAsync(body, cancellationToken);
        return score is { } s && s.Probability >= settings.Threshold ? s : null;
    }
}
