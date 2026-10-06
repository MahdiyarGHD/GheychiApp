using Gheychi.Core.Spam;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SpamDetectorTests
{
    private sealed class FakeClassifier(SpamScore? score) : ISpamClassifier
    {
        public int Calls { get; private set; }

        public Task<SpamScore?> ScoreAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(score);
        }

        public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettings(float threshold) : ISpamSettings
    {
        public float Threshold { get; set; } = threshold;
        public int RetentionDays { get; set; } = SpamDetector.DefaultRetentionDays;
    }

    private sealed class FakeTrustedSenders(params string[] trusted) : ITrustedSenders
    {
        public bool IsTrusted(string address) => trusted.Contains(address);

        public void SetTrusted(string address, bool isTrusted)
        {
        }
    }

    private static SpamDetector Detector(FakeClassifier classifier, float threshold, params string[] trusted) =>
        new(classifier, new FakeSettings(threshold), new FakeTrustedSenders(trusted));

    [Fact]
    public async Task ScoreAtThreshold_IsSpam()
    {
        var detector = Detector(new FakeClassifier(new SpamScore(0.85f, 1)), 0.85f);

        Assert.Equal(new SpamVerdict(new SpamScore(0.85f, 1), 0.85f), await detector.DetectAsync("+989121234567", "win a prize", fromContact: false));
    }

    [Fact]
    public async Task ScoreBelowThreshold_IsNotSpam()
    {
        var detector = Detector(new FakeClassifier(new SpamScore(0.84f, 1)), 0.85f);

        Assert.Null(await detector.DetectAsync("+989121234567", "see you soon", fromContact: false));
    }

    [Fact]
    public async Task Contact_IsNeverClassified()
    {
        var classifier = new FakeClassifier(new SpamScore(1f, 1));
        var detector = Detector(classifier, 0.5f);

        Assert.Null(await detector.DetectAsync("+989121234567", "win a prize", fromContact: true));
        Assert.Equal(0, classifier.Calls);
    }

    [Fact]
    public async Task TrustedSender_IsNeverClassified()
    {
        var classifier = new FakeClassifier(new SpamScore(1f, 1));
        var detector = Detector(classifier, 0.5f, "Bank");

        Assert.Null(await detector.DetectAsync("Bank", "win a prize", fromContact: false));
        Assert.Equal(0, classifier.Calls);
    }

    [Fact]
    public async Task NoModel_IsNotSpam()
    {
        var detector = Detector(new FakeClassifier(null), 0.5f);

        Assert.Null(await detector.DetectAsync("+989121234567", "win a prize", fromContact: false));
    }

    [Theory]
    [InlineData(0.85f, SpamConfidenceLevel.Likely, 85)]
    [InlineData(0.9499f, SpamConfidenceLevel.Likely, 94)]
    [InlineData(0.95f, SpamConfidenceLevel.VeryLikely, 95)]
    [InlineData(1f, SpamConfidenceLevel.VeryLikely, 99)]
    public void Confidence_LevelAndPercent(float probability, SpamConfidenceLevel level, int percent)
    {
        Assert.Equal(level, SpamConfidence.Level(probability));
        Assert.Equal(percent, SpamConfidence.Percent(probability));
    }
}
