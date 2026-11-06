using System.Diagnostics;
using Gheychi.Core.Services;

namespace Gheychi.Core.Spam;

/// <param name="Found">Messages so far that are spam by the user's threshold.</param>
public readonly record struct SpamScanProgress(int Checked, int Total, int Found);

/// <param name="Skipped">Messages never judged: contacts, trusted senders, reactions, ones the user kept before.</param>
/// <param name="ModelFailed">No model could be loaded, so nothing was judged; not a clean result.</param>
public sealed record SpamScanResult(
    IReadOnlyList<SpamScanCandidate> Candidates,
    int Total,
    int Checked,
    int Unchecked,
    int Skipped,
    bool Stopped,
    bool ModelFailed);

/// <summary>Judges the messages already on the phone. It changes nothing: it lists what the user may then choose to move.</summary>
public sealed class SpamHistoryScan(ISpamClassifier classifier, SpamDetector detector)
{
    /// <summary>
    /// Nothing below this is selected, whatever the user's threshold: hiding a real message from years of history costs
    /// more than leaving an old ad where it is.
    /// </summary>
    public const float MinBar = 0.90f;

    /// <summary>A sender's other messages scoring at least this are listed beside its strong hits; spammers rarely send one.</summary>
    public const float SenderContextFrom = 0.5f;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    public static float Bar(float threshold) => Math.Max(threshold, MinBar);

    /// <summary>Identifies a received message across the SMS store and the quarantine, whose row ids differ.</summary>
    public static string Fingerprint(string address, long dateMillis) => $"{PhoneNumberNormalizer.ToLookupKey(address)}|{dateMillis}";

    /// <param name="skip"><see cref="Fingerprint"/>s of messages the user already said are not spam.</param>
    /// <param name="threshold">The user's spam threshold.</param>
    public async Task<SpamScanResult> RunAsync(
        IReadOnlyList<ReceivedSms> messages,
        IReadOnlySet<string> skip,
        float threshold,
        IProgress<SpamScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var bar = Bar(threshold);
        var scored = new List<(ReceivedSms Sms, SpamScore Score)>();
        // Campaigns repeat word for word; the model gives the same text the same score.
        var cache = new Dictionary<string, SpamScore?>(StringComparer.Ordinal);
        int checkedCount = 0, unscored = 0, skipped = 0, found = 0;
        var stopped = false;
        var clock = Stopwatch.StartNew();

        try
        {
            // Null for an empty text means there is no model at all: every message would come back unscored and the
            // scan would look clean.
            // Not cancellable: it can be what loads the model, and a load cut short is not retried until the app restarts.
            if (await classifier.ScoreAsync(string.Empty, CancellationToken.None) is null)
                return new SpamScanResult([], messages.Count, 0, 0, 0, Stopped: false, ModelFailed: true);

            foreach (var sms in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (detector.IsExempt(sms.Address, sms.Body, sms.FromContact) || skip.Contains(Fingerprint(sms.Address, sms.DateMillis)))
                {
                    skipped++;
                }
                else
                {
                    if (!cache.TryGetValue(sms.Body, out var score))
                        cache[sms.Body] = score = await classifier.ScoreAsync(sms.Body, cancellationToken);

                    if (score is { } s)
                    {
                        scored.Add((sms, s));
                        if (s.Probability >= threshold)
                            found++;
                    }
                    else
                    {
                        unscored++;
                    }
                }

                checkedCount++;
                if (clock.Elapsed >= ProgressInterval)
                {
                    clock.Restart();
                    progress?.Report(new SpamScanProgress(checkedCount, messages.Count, found));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopped = true;
        }

        progress?.Report(new SpamScanProgress(checkedCount, messages.Count, found));
        return new SpamScanResult(Pick(scored, threshold, bar), messages.Count, checkedCount, unscored, skipped, stopped, ModelFailed: false);
    }

    private static List<SpamScanCandidate> Pick(List<(ReceivedSms Sms, SpamScore Score)> scored, float threshold, float bar)
    {
        var strongSenders = scored
            .Where(x => x.Score.Probability >= bar)
            .Select(x => PhoneNumberNormalizer.ToLookupKey(x.Sms.Address))
            .ToHashSet(StringComparer.Ordinal);

        var picked = new List<SpamScanCandidate>();
        foreach (var (sms, score) in scored)
        {
            var p = score.Probability;
            SpamScanSection? section =
                p >= bar ? SpamScanSection.Strong
                : p >= threshold ? SpamScanSection.Maybe
                : p >= SenderContextFrom && strongSenders.Contains(PhoneNumberNormalizer.ToLookupKey(sms.Address)) ? SpamScanSection.Sender
                : null;

            if (section is { } s)
                picked.Add(new SpamScanCandidate(sms, p, score.ModelVersion, s, Selected: s == SpamScanSection.Strong));
        }

        return picked;
    }
}
