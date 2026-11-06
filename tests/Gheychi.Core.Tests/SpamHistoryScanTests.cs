using Gheychi.Core.Spam;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SpamHistoryScanTests
{
    private sealed class FakeClassifier(Dictionary<string, float> scores, bool loaded = true) : ISpamClassifier
    {
        public List<string> Scored { get; } = [];

        public Func<string, CancellationToken, Task>? OnScore { get; set; }

        public async Task<SpamScore?> ScoreAsync(string text, CancellationToken cancellationToken = default)
        {
            if (OnScore is not null)
                await OnScore(text, cancellationToken);
            if (!loaded)
                return null;
            if (text.Length == 0)
                return new SpamScore(0, 3);

            Scored.Add(text);
            return scores.TryGetValue(text, out var p) ? new SpamScore(p, 3) : null;
        }

        public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSettings : ISpamSettings
    {
        public bool Enabled { get; set; } = true;
        public float Threshold { get; set; } = 0.85f;
        public int RetentionDays { get; set; } = 30;
    }

    private sealed class FakeTrustedSenders(params string[] trusted) : ITrustedSenders
    {
        public bool IsTrusted(string address) => trusted.Contains(address);

        public void SetTrusted(string address, bool isTrusted)
        {
        }

        public IReadOnlyList<string> GetAll() => trusted;
    }

    private static SpamHistoryScan Scan(FakeClassifier classifier, params string[] trusted) =>
        new(classifier, new SpamDetector(classifier, new FakeSettings(), new FakeTrustedSenders(trusted)));

    private static ReceivedSms Sms(long id, string address, string body, bool fromContact = false) =>
        new(id, 1, address, body, 1_760_000_000_000 + id, 1, Unread: false, fromContact);

    [Fact]
    public async Task SortsHitsIntoSections_AndSelectsOnlyStrongOnes()
    {
        var classifier = new FakeClassifier(new() { ["ad"] = 0.97f, ["maybe"] = 0.87f, ["hello"] = 0.2f });

        var result = await Scan(classifier).RunAsync(
            [Sms(1, "5000", "ad"), Sms(2, "6000", "maybe"), Sms(3, "7000", "hello")], new HashSet<string>(), 0.85f);

        Assert.Equal([(1L, SpamScanSection.Strong, true), (2L, SpamScanSection.Maybe, false)],
            result.Candidates.Select(c => (c.Sms.Id, c.Section, c.Selected)));
        Assert.Equal(3, result.Checked);
        Assert.False(result.Stopped);
    }

    [Fact]
    public async Task BarIsNeverBelowNinety()
    {
        Assert.Equal(0.90f, SpamHistoryScan.Bar(0.6f));
        Assert.Equal(0.95f, SpamHistoryScan.Bar(0.95f));

        var classifier = new FakeClassifier(new() { ["ad"] = 0.89f });
        var result = await Scan(classifier).RunAsync([Sms(1, "5000", "ad")], new HashSet<string>(), 0.6f);

        Assert.Equal(SpamScanSection.Maybe, Assert.Single(result.Candidates).Section);
    }

    [Fact]
    public async Task ListsSuspiciousMessagesFromStrongSenders_Unselected()
    {
        var classifier = new FakeClassifier(new() { ["ad"] = 0.99f, ["promo"] = 0.6f, ["other"] = 0.6f, ["low"] = 0.3f });

        var result = await Scan(classifier).RunAsync(
            [Sms(1, "5000", "ad"), Sms(2, "5000", "promo"), Sms(3, "5000", "low"), Sms(4, "6000", "other")], new HashSet<string>(), 0.85f);

        Assert.Equal([(1L, SpamScanSection.Strong, true), (2L, SpamScanSection.Sender, false)],
            result.Candidates.Select(c => (c.Sms.Id, c.Section, c.Selected)));
    }

    [Fact]
    public async Task SkipsContactsTrustedReactionsAndKeptMessages()
    {
        var classifier = new FakeClassifier(new() { ["ad"] = 0.99f });
        var kept = Sms(4, "8000", "ad");

        var result = await Scan(classifier, "7000").RunAsync(
            [Sms(1, "5000", "ad", fromContact: true), Sms(2, "7000", "ad"), Sms(3, "6000", "Liked “ad”"), kept, Sms(5, "9000", "ad")],
            new HashSet<string> { SpamHistoryScan.Fingerprint(kept.Address, kept.DateMillis) },
            0.85f);

        Assert.Equal([5L], result.Candidates.Select(c => c.Sms.Id));
        Assert.Equal(4, result.Skipped);
        Assert.Equal(5, result.Checked);
    }

    [Fact]
    public async Task NoModel_IsAFailureNotACleanResult()
    {
        var result = await Scan(new FakeClassifier([], loaded: false)).RunAsync([Sms(1, "5000", "ad")], new HashSet<string>(), 0.85f);

        Assert.True(result.ModelFailed);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.Checked);
    }

    [Fact]
    public async Task MessageTheModelCannotScore_IsCountedUnchecked()
    {
        var result = await Scan(new FakeClassifier(new() { ["ad"] = 0.99f })).RunAsync(
            [Sms(1, "5000", "ad"), Sms(2, "5000", "unknown")], new HashSet<string>(), 0.85f);

        Assert.Equal(1, result.Unchecked);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public async Task SameTextIsScoredOnce()
    {
        var classifier = new FakeClassifier(new() { ["ad"] = 0.99f });

        var result = await Scan(classifier).RunAsync([Sms(1, "5000", "ad"), Sms(2, "6000", "ad")], new HashSet<string>(), 0.85f);

        Assert.Equal(["ad"], classifier.Scored);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task Stop_KeepsWhatWasFoundSoFar()
    {
        using var stop = new CancellationTokenSource();
        var classifier = new FakeClassifier(new() { ["ad"] = 0.99f, ["ad2"] = 0.99f, ["ad3"] = 0.99f });
        classifier.OnScore = (text, _) =>
        {
            if (text == "ad2")
                stop.Cancel();
            return Task.CompletedTask;
        };

        var result = await Scan(classifier).RunAsync(
            [Sms(1, "5000", "ad"), Sms(2, "5000", "ad2"), Sms(3, "5000", "ad3")], new HashSet<string>(), 0.85f, null, stop.Token);

        Assert.True(result.Stopped);
        Assert.Equal(2, result.Checked);
        Assert.Equal([1L, 2L], result.Candidates.Select(c => c.Sms.Id));
    }
}
