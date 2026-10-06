namespace Gheychi.Core.Spam;

public sealed class SpamDetector(ISpamClassifier classifier, ISpamSettings settings, ITrustedSenders trustedSenders)
{
    public const float DefaultThreshold = 0.85f;
    public const int DefaultRetentionDays = 30;

    /// <summary>The score when the message is spam; null when it is not, or when it could not be classified.</summary>
    public async Task<SpamScore?> DetectAsync(string address, string body, bool fromContact, CancellationToken cancellationToken = default)
    {
        if (fromContact || string.IsNullOrWhiteSpace(body) || trustedSenders.IsTrusted(address))
            return null;

        var score = await classifier.ScoreAsync(body, cancellationToken);
        return score is { } s && s.Probability >= settings.Threshold ? s : null;
    }
}
