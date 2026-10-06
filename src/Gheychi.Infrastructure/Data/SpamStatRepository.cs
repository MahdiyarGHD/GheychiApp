using Gheychi.Core.Spam;
using SQLite;

namespace Gheychi.Infrastructure.Data;

public sealed class SpamStatRepository : ISpamStatsRepository
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SpamStatRepository(string databasePath)
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
                await _db.CreateTableAsync<SpamStatEntity>();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task AddAsync(SpamStat stat)
    {
        try
        {
            await EnsureInitializedAsync();
            await _db.InsertAsync(ToEntity(stat));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logging spam failed: {ex}");
        }
    }

    public async Task AddMissingAsync(IReadOnlyCollection<SpamStat> stats)
    {
        if (stats.Count == 0)
            return;

        try
        {
            await EnsureInitializedAsync();
            var logged = (await _db.QueryScalarsAsync<long>("SELECT SpamMessageId FROM SpamStat")).ToHashSet();
            var missing = stats.Where(s => !logged.Contains(s.SpamMessageId)).Select(ToEntity).ToList();
            if (missing.Count > 0)
                await _db.InsertAllAsync(missing);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Logging spam failed: {ex}");
        }
    }

    public async Task MarkRestoredAsync(long spamMessageId)
    {
        try
        {
            await EnsureInitializedAsync();
            await _db.ExecuteAsync("UPDATE SpamStat SET Restored = 1 WHERE SpamMessageId = ?", spamMessageId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Marking spam restored failed: {ex}");
        }
    }

    public async Task<IReadOnlyList<SpamStat>> GetAllAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            var entities = await _db.Table<SpamStatEntity>().ToListAsync();
            return entities.Select(e => new SpamStat(
                e.Id,
                e.SpamMessageId,
                e.SenderKey,
                DateTimeOffset.FromUnixTimeMilliseconds(e.TimestampMillis).LocalDateTime,
                e.Score,
                e.ModelVersion,
                e.Restored)).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading spam stats failed: {ex}");
            return Array.Empty<SpamStat>();
        }
    }

    private static SpamStatEntity ToEntity(SpamStat stat) =>
        new()
        {
            SpamMessageId = stat.SpamMessageId,
            SenderKey = stat.SenderKey,
            TimestampMillis = new DateTimeOffset(stat.Timestamp).ToUnixTimeMilliseconds(),
            Score = stat.Score,
            ModelVersion = stat.ModelVersion,
            Restored = stat.Restored
        };
}
