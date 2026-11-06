namespace Gheychi.Core.Spam;

/// <summary>A received SMS as the history scan reads it from the phone's message store.</summary>
public sealed record ReceivedSms(long Id, long ThreadId, string Address, string Body, long DateMillis, int SubId, bool Unread, bool FromContact);

public enum SpamScanSection
{
    /// <summary>At or above the scan's bar: selected for moving.</summary>
    Strong,

    /// <summary>Spam by the user's threshold but under the scan's bar: listed, not selected.</summary>
    Maybe,

    /// <summary>Not spam by the threshold, but from a sender with strong hits and still suspicious: listed, not selected.</summary>
    Sender
}

public sealed record SpamScanCandidate(ReceivedSms Sms, float Score, int ModelVersion, SpamScanSection Section, bool Selected);

/// <summary>One run of the history scan. <see cref="AppliedAt"/> is null while its results wait for review.</summary>
/// <param name="Total">Received messages there were to check.</param>
/// <param name="Checked">How many were looked at before the scan ended or was stopped, skipped ones included.</param>
/// <param name="Unchecked">How many the model failed to score.</param>
public sealed record SpamScanRecord(long Id, DateTime StartedAt, DateTime? AppliedAt, int Total, int Checked, int Unchecked, bool Stopped);

public interface ISpamScanStore
{
    /// <summary>Saves a finished scan for review, replacing any other one still waiting. Returns its id, or 0 on failure.</summary>
    Task<long> SavePendingAsync(SpamScanRecord scan, IReadOnlyList<SpamScanCandidate> candidates);

    /// <summary>The scan waiting for review, if any.</summary>
    Task<(SpamScanRecord Scan, IReadOnlyList<SpamScanCandidate> Candidates)?> GetPendingAsync();

    Task SetSelectedAsync(long scanId, IReadOnlyCollection<long> smsIds, bool selected);

    Task DiscardPendingAsync();

    /// <summary>The review is done and its messages move: the scan becomes a batch in the Spam tab. False when it could not be saved.</summary>
    Task<bool> MarkAppliedAsync(long scanId, DateTime appliedAt);

    /// <summary>Scans whose messages were moved, newest first.</summary>
    Task<IReadOnlyList<SpamScanRecord>> GetAppliedAsync();

    Task DeleteAsync(IEnumerable<long> scanIds);

    /// <summary>Remembers messages the user said are not spam, so later scans leave them out.</summary>
    Task KeepAsync(IReadOnlyCollection<(string Address, long DateMillis)> messages);

    /// <summary><see cref="SpamHistoryScan.Fingerprint"/>s of the kept messages.</summary>
    Task<IReadOnlySet<string>> GetKeptAsync();
}
