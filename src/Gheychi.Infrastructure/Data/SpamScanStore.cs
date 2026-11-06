using Gheychi.Core.Spam;
using SQLite;

namespace Gheychi.Infrastructure.Data;

[Table("SpamScan")]
public sealed class SpamScanEntity
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public long StartedAtMillis { get; set; }

    // 0 while the scan waits for review.
    public long AppliedAtMillis { get; set; }

    public int Total { get; set; }

    public int Checked { get; set; }

    public int Unchecked { get; set; }

    public bool Stopped { get; set; }
}

[Table("SpamScanCandidate")]
public sealed class SpamScanCandidateEntity
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    [Indexed]
    public long ScanId { get; set; }

    public long SmsId { get; set; }

    public long ThreadId { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public long DateMillis { get; set; }

    public int SubId { get; set; }

    public bool Unread { get; set; }

    public float Score { get; set; }

    public int ModelVersion { get; set; }

    public int Section { get; set; }

    public bool Selected { get; set; }
}

[Table("SpamScanKept")]
public sealed class SpamScanKeptEntity
{
    [PrimaryKey]
    public string Fingerprint { get; set; } = string.Empty;
}

public sealed class SpamScanStore : ISpamScanStore
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SpamScanStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _db = new SQLiteAsyncConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (!_initialized)
            {
                await _db.CreateTableAsync<SpamScanEntity>();
                await _db.CreateTableAsync<SpamScanCandidateEntity>();
                await _db.CreateTableAsync<SpamScanKeptEntity>();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<long> SavePendingAsync(SpamScanRecord scan, IReadOnlyList<SpamScanCandidate> candidates)
    {
        try
        {
            await EnsureInitializedAsync();
            var entity = new SpamScanEntity
            {
                StartedAtMillis = ToMillis(scan.StartedAt),
                Total = scan.Total,
                Checked = scan.Checked,
                Unchecked = scan.Unchecked,
                Stopped = scan.Stopped
            };

            await _db.RunInTransactionAsync(db =>
            {
                DiscardPending(db);
                db.Insert(entity);
                db.InsertAll(candidates.Select(c => new SpamScanCandidateEntity
                {
                    ScanId = entity.Id,
                    SmsId = c.Sms.Id,
                    ThreadId = c.Sms.ThreadId,
                    Address = c.Sms.Address,
                    Body = c.Sms.Body,
                    DateMillis = c.Sms.DateMillis,
                    SubId = c.Sms.SubId,
                    Unread = c.Sms.Unread,
                    Score = c.Score,
                    ModelVersion = c.ModelVersion,
                    Section = (int)c.Section,
                    Selected = c.Selected
                }), runInTransaction: false);
            });
            return entity.Id;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving the spam scan failed: {ex}");
            return 0;
        }
    }

    public async Task<(SpamScanRecord Scan, IReadOnlyList<SpamScanCandidate> Candidates)?> GetPendingAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            var scan = await _db.Table<SpamScanEntity>().Where(s => s.AppliedAtMillis == 0).OrderByDescending(s => s.Id).FirstOrDefaultAsync();
            if (scan is null)
                return null;

            var rows = await _db.Table<SpamScanCandidateEntity>().Where(c => c.ScanId == scan.Id).OrderBy(c => c.Id).ToListAsync();
            var candidates = rows.Select(c => new SpamScanCandidate(
                new ReceivedSms(c.SmsId, c.ThreadId, c.Address, c.Body, c.DateMillis, c.SubId, c.Unread, FromContact: false),
                c.Score,
                c.ModelVersion,
                (SpamScanSection)c.Section,
                c.Selected)).ToList();
            return (ToRecord(scan), candidates);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the spam scan failed: {ex}");
            return null;
        }
    }

    public async Task SetSelectedAsync(long scanId, IReadOnlyCollection<long> smsIds, bool selected)
    {
        try
        {
            await EnsureInitializedAsync();
            foreach (var chunk in smsIds.Distinct().Chunk(400))
            {
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                await _db.ExecuteAsync(
                    $"UPDATE SpamScanCandidate SET Selected = ? WHERE ScanId = ? AND SmsId IN ({placeholders})",
                    [selected, scanId, .. chunk.Cast<object>()]);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving the scan selection failed: {ex}");
        }
    }

    public async Task DiscardPendingAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            await _db.RunInTransactionAsync(DiscardPending);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discarding the spam scan failed: {ex}");
        }
    }

    public async Task<bool> MarkAppliedAsync(long scanId, DateTime appliedAt)
    {
        try
        {
            await EnsureInitializedAsync();
            var updated = 0;
            await _db.RunInTransactionAsync(db =>
            {
                updated = db.Execute("UPDATE SpamScan SET AppliedAtMillis = ? WHERE Id = ?", ToMillis(appliedAt), scanId);
                db.Execute("DELETE FROM SpamScanCandidate WHERE ScanId = ?", scanId);
            });
            return updated == 1;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Marking the spam scan applied failed: {ex}");
            return false;
        }
    }

    public async Task<IReadOnlyList<SpamScanRecord>> GetAppliedAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            var scans = await _db.Table<SpamScanEntity>().Where(s => s.AppliedAtMillis > 0).OrderByDescending(s => s.AppliedAtMillis).ToListAsync();
            return scans.Select(ToRecord).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading spam scans failed: {ex}");
            return [];
        }
    }

    public async Task DeleteAsync(IEnumerable<long> scanIds)
    {
        try
        {
            await EnsureInitializedAsync();
            foreach (var chunk in scanIds.Distinct().Chunk(400))
            {
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                var args = chunk.Cast<object>().ToArray();
                await _db.ExecuteAsync($"DELETE FROM SpamScanCandidate WHERE ScanId IN ({placeholders})", args);
                await _db.ExecuteAsync($"DELETE FROM SpamScan WHERE Id IN ({placeholders})", args);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deleting spam scans failed: {ex}");
        }
    }

    public async Task KeepAsync(IReadOnlyCollection<(string Address, long DateMillis)> messages)
    {
        if (messages.Count == 0)
            return;

        try
        {
            await EnsureInitializedAsync();
            await _db.RunInTransactionAsync(db =>
            {
                foreach (var (address, dateMillis) in messages)
                    db.InsertOrReplace(new SpamScanKeptEntity { Fingerprint = SpamHistoryScan.Fingerprint(address, dateMillis) });
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Remembering kept messages failed: {ex}");
        }
    }

    public async Task<IReadOnlySet<string>> GetKeptAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            return (await _db.QueryScalarsAsync<string>("SELECT Fingerprint FROM SpamScanKept")).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading kept messages failed: {ex}");
            return new HashSet<string>();
        }
    }

    private static void DiscardPending(SQLiteConnection db)
    {
        db.Execute("DELETE FROM SpamScanCandidate WHERE ScanId IN (SELECT Id FROM SpamScan WHERE AppliedAtMillis = 0)");
        db.Execute("DELETE FROM SpamScan WHERE AppliedAtMillis = 0");
    }

    private static long ToMillis(DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();

    private static DateTime FromMillis(long millis) => DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime;

    private static SpamScanRecord ToRecord(SpamScanEntity e) =>
        new(e.Id, FromMillis(e.StartedAtMillis), e.AppliedAtMillis > 0 ? FromMillis(e.AppliedAtMillis) : null, e.Total, e.Checked, e.Unchecked, e.Stopped);
}
