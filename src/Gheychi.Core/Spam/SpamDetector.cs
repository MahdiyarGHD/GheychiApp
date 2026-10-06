namespace Gheychi.Core.Spam;

/// <summary>A message judged spam: its score and the threshold in force when it was judged.</summary>
public readonly record struct SpamVerdict(SpamScore Score, float Threshold);

public sealed class SpamDetector(ISpamClassifier classifier, ISpamSettings settings, ITrustedSenders trustedSenders)
{
    public const float DefaultThreshold = 0.85f;
    public const int DefaultRetentionDays = 30;

    /// <summary>The verdict when the message is spam; null when it is not, or when it could not be classified.</summary>
    public async Task<SpamVerdict?> DetectAsync(string address, string body, bool fromContact, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || fromContact || string.IsNullOrWhiteSpace(body) || trustedSenders.IsTrusted(address))
            return null;

        var threshold = settings.Threshold;
        var score = await classifier.ScoreAsync(body, cancellationToken);
        return score is { } s && s.Probability >= threshold ? new SpamVerdict(s, threshold) : null;
    }
}
