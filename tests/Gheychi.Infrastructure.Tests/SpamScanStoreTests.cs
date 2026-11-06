using Gheychi.Core.Spam;
using Gheychi.Infrastructure.Data;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class SpamScanStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"gheychi_scan_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }
    }

    private static SpamScanCandidate Candidate(long smsId, SpamScanSection section) =>
        new(new ReceivedSms(smsId, 7, "5000", $"body {smsId}", 1_760_000_000_000 + smsId, 2, Unread: true, FromContact: false),
            0.95f, 4, section, section == SpamScanSection.Strong);

    [Fact]
    public async Task PendingScan_RoundTripsAndKeepsSelection()
    {
        var store = new SpamScanStore(_dbPath);
        var started = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Local);

        var id = await store.SavePendingAsync(new SpamScanRecord(0, started, null, 100, 60, 1, Stopped: true),
            [Candidate(1, SpamScanSection.Strong), Candidate(2, SpamScanSection.Maybe)]);
        await store.SetSelectedAsync(id, [1], false);
        await store.SetSelectedAsync(id, [2], true);

        var pending = await store.GetPendingAsync();

        Assert.NotNull(pending);
        Assert.Equal(new SpamScanRecord(id, started, null, 100, 60, 1, true), pending.Value.Scan);
        Assert.Equal([(1L, false), (2L, true)], pending.Value.Candidates.Select(c => (c.Sms.Id, c.Selected)));
        Assert.Equal(Candidate(2, SpamScanSection.Maybe).Sms, pending.Value.Candidates[1].Sms);
    }

    [Fact]
    public async Task NewPendingScan_ReplacesTheOldOne_AndAppliedScansStay()
    {
        var store = new SpamScanStore(_dbPath);
        var applied = await store.SavePendingAsync(new SpamScanRecord(0, DateTime.Now, null, 1, 1, 0, false), [Candidate(1, SpamScanSection.Strong)]);
        var appliedAt = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Local);
        Assert.True(await store.MarkAppliedAsync(applied, appliedAt));

        await store.SavePendingAsync(new SpamScanRecord(0, DateTime.Now, null, 2, 2, 0, false), [Candidate(2, SpamScanSection.Strong)]);
        var second = await store.SavePendingAsync(new SpamScanRecord(0, DateTime.Now, null, 3, 3, 0, false), [Candidate(3, SpamScanSection.Strong)]);

        var pending = await store.GetPendingAsync();
        Assert.Equal(second, pending!.Value.Scan.Id);
        Assert.Equal([3L], pending.Value.Candidates.Select(c => c.Sms.Id));
        Assert.Equal(appliedAt, Assert.Single(await store.GetAppliedAsync()).AppliedAt);

        await store.DiscardPendingAsync();
        Assert.Null(await store.GetPendingAsync());

        await store.DeleteAsync([applied]);
        Assert.Empty(await store.GetAppliedAsync());
    }

    [Fact]
    public async Task KeptMessages_AreRememberedByFingerprint()
    {
        var store = new SpamScanStore(_dbPath);

        await store.KeepAsync([("+989121234567", 5), ("+989121234567", 5), ("Bank", 6)]);

        var kept = await store.GetKeptAsync();
        Assert.Equal(2, kept.Count);
        Assert.Contains(SpamHistoryScan.Fingerprint("09121234567", 5), kept);
    }
}
