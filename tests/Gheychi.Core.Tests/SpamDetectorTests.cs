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
    }

    private sealed class FakeSettings(float threshold) : ISpamSettings
    {
        public float Threshold { get; set; } = threshold;
    }

    [Fact]
    public async Task ScoreAtThreshold_IsSpam()
    {
        var detector = new SpamDetector(new FakeClassifier(new SpamScore(0.85f, 1)), new FakeSettings(0.85f));

        Assert.Equal(new SpamScore(0.85f, 1), await detector.DetectAsync("win a prize", fromContact: false));
    }

    [Fact]
    public async Task ScoreBelowThreshold_IsNotSpam()
    {
        var detector = new SpamDetector(new FakeClassifier(new SpamScore(0.84f, 1)), new FakeSettings(0.85f));

        Assert.Null(await detector.DetectAsync("see you soon", fromContact: false));
    }

    [Fact]
    public async Task Contact_IsNeverClassified()
    {
        var classifier = new FakeClassifier(new SpamScore(1f, 1));
        var detector = new SpamDetector(classifier, new FakeSettings(0.5f));

        Assert.Null(await detector.DetectAsync("win a prize", fromContact: true));
        Assert.Equal(0, classifier.Calls);
    }

    [Fact]
    public async Task NoModel_IsNotSpam()
    {
        var detector = new SpamDetector(new FakeClassifier(null), new FakeSettings(0.5f));

        Assert.Null(await detector.DetectAsync("win a prize", fromContact: false));
    }
}
